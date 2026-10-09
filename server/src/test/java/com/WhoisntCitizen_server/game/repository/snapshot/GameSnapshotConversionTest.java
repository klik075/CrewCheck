package com.WhoisntCitizen_server.game.repository.snapshot;

import com.WhoisntCitizen_server.common.exception.GameRuleException;
import com.WhoisntCitizen_server.game.entity.DeathCause;
import com.WhoisntCitizen_server.game.entity.Game;
import com.WhoisntCitizen_server.game.entity.GamePhase;
import com.WhoisntCitizen_server.game.entity.GamePlayer;
import com.WhoisntCitizen_server.jobs.domain.ActionCode;
import com.WhoisntCitizen_server.jobs.domain.Faction;
import com.WhoisntCitizen_server.support.TestRoles;
import org.junit.jupiter.api.Test;
import tools.jackson.databind.json.JsonMapper;

import java.time.Instant;
import java.util.List;

import static org.assertj.core.api.Assertions.assertThat;
import static org.assertj.core.api.Assertions.assertThatThrownBy;

/**
 * 1-2: Game → GameSnapshot → (JSON) → Game 변환 기본 확인.
 * 모든 필드를 채운 게임으로 빠진 필드를 잡는 테스트는 game.entity.GameSnapshotFullRoundTripTest(1-3)에 있다.
 */
class GameSnapshotConversionTest {

    private static final Instant NOW = Instant.parse("2026-10-01T12:00:00Z");

    private final JsonMapper jsonMapper = JsonMapper.builder().build();

    // 1: 해적, 2: 앵무새, 3: 선장, 4: 선원, 5: 선의
    private static Game newGame() {
        return new Game("7", List.of(
                new GamePlayer(1L, "해적", TestRoles.RAIDER),
                new GamePlayer(2L, "앵무새", TestRoles.PARROT),
                new GamePlayer(3L, "선장", TestRoles.CAPTAIN),
                new GamePlayer(4L, "선원", TestRoles.SAILOR),
                new GamePlayer(5L, "선의", TestRoles.DOCTOR)), true);
    }

    /** 저장소가 하는 일과 같은 경로: 스냅샷 → JSON → 스냅샷 → Game */
    private Game roundTrip(Game game) {
        String json = jsonMapper.writeValueAsString(GameSnapshot.from(game));
        return jsonMapper.readValue(json, GameSnapshot.class).toGame();
    }

    @Test
    void 새_게임은_id와_생성_시각과_입장_순서가_그대로_되살아난다() {
        Game original = newGame();

        Game restored = roundTrip(original);

        assertThat(restored).isNotSameAs(original);
        assertThat(restored.getGameId()).isEqualTo(original.getGameId());
        assertThat(restored.getCreatedAt()).isEqualTo(original.getCreatedAt());
        assertThat(restored.getPhase()).isNull();
        assertThat(restored.getPlayers()).extracting(GamePlayer::getPlayerId).containsExactly(1L, 2L, 3L, 4L, 5L);
        assertThat(restored).usingRecursiveComparison().isEqualTo(original);
    }

    @Test
    void 밤의_숨은_상태도_되살아난다() {
        Game original = newGame();
        original.changePhase(GamePhase.NIGHT, NOW.plusSeconds(30));
        original.recordNightAction(1L, 4L);                 // 해적 → 선원
        original.recordNightAction(2L, 1L, NOW);            // 앵무새 접선 → 행동 확정(lockedActors)
        original.skipNightAction(3L);                       // 선장 넘기기(skippedActors)

        Game restored = roundTrip(original);

        assertThat(restored).usingRecursiveComparison().isEqualTo(original);
        assertThat(restored.getPlayer(2L).isContacted()).isTrue();
        // 확정된 앵무새는 되살린 뒤에도 행동을 바꿀 수 없다
        assertThatThrownBy(() -> restored.recordNightAction(2L, 4L, NOW)).isInstanceOf(GameRuleException.class);
        // 넘기기가 남아 있어 선의만 내면 전원 제출이 된다
        restored.recordNightAction(5L, 4L);
        assertThat(restored.allNightActionsSubmitted()).isTrue();
    }

    @Test
    void 투표의_숨은_상태와_순서가_되살아난다() {
        Game original = newGame();
        original.changePhase(GamePhase.NIGHT, NOW.plusSeconds(30));
        original.changePhase(GamePhase.VOTE, NOW.plusSeconds(60));
        original.banVote(5L);                               // 요리사 투표 금지(voteBanned)
        original.recordVote(3L, 1L, false);                 // 임시 선택
        original.recordVote(1L, 3L, true);                  // 투표 완료(confirmedVoters)

        Game restored = roundTrip(original);

        assertThat(restored).usingRecursiveComparison().isEqualTo(original);
        assertThat(restored.getVotes().keySet()).containsExactly(3L, 1L);
        assertThat(restored.hasConfirmedVote(1L)).isTrue();
        assertThat(restored.hasConfirmedVote(3L)).isFalse();
        assertThat(restored.isVoteBanned(5L)).isTrue();
    }

    @Test
    void 낮_토론_넘기기가_되살아난다() {
        Game original = newGame();
        original.changePhase(GamePhase.NIGHT, NOW.plusSeconds(30));
        original.changePhase(GamePhase.DAY, NOW.plusSeconds(60));
        original.skipDay(4L);                               // 낮 토론 넘기기(daySkippers)
        original.skipDay(2L);

        Game restored = roundTrip(original);

        assertThat(restored).usingRecursiveComparison().isEqualTo(original);
        assertThat(restored.hasSkippedDay(4L)).isTrue();
        assertThat(restored.hasSkippedDay(1L)).isFalse();
        assertThat(restored.daySkipCount()).isEqualTo(2);
        // 이미 넘긴 사람이 다시 넘기면 false (중복으로 세지 않음)
        assertThat(restored.skipDay(4L)).isFalse();
        restored.skipDay(1L);
        restored.skipDay(3L);
        restored.skipDay(5L);
        assertThat(restored.allDaySkipped()).isTrue();
    }

    @Test
    void 플레이어의_숨은_기록도_되살아난다() {
        Game original = newGame();
        original.changePhase(GamePhase.NIGHT, NOW.plusSeconds(30));
        GamePlayer captain = original.getPlayer(3L);
        captain.recordUse(ActionCode.READ_CORPSE_ROLE);
        captain.recordSelfProtect(1);
        captain.recordVoteBan(1, 4L);
        captain.recordSeduceSuccess(1);
        captain.addKrakenMark(5L);
        captain.addKrakenMark(4L);
        captain.fakeFactionOf(1L, () -> Faction.CREW);
        captain.fakeCorpseRoleOf(4L, () -> TestRoles.DOCTOR);
        original.getPlayer(4L).kill(DeathCause.ATTACK);
        original.recordDeath();

        Game restored = roundTrip(original);

        assertThat(restored).usingRecursiveComparison().isEqualTo(original);
        GamePlayer restoredCaptain = restored.getPlayer(3L);
        assertThat(restoredCaptain.remainingUses(ActionCode.READ_CORPSE_ROLE)).isEqualTo(1);
        assertThat(restoredCaptain.getKrakenMarks()).containsExactly(5L, 4L);
        // 원숭이 가짜 결과는 처음 정한 값을 계속 쓴다 (draw가 다시 불리지 않음)
        assertThat(restoredCaptain.fakeFactionOf(1L, () -> Faction.PIRATE)).isEqualTo(Faction.CREW);
        assertThat(restored.getPlayer(4L).getDeathCause()).isEqualTo(DeathCause.ATTACK);
        assertThat(restored.daysWithoutDeath()).isZero();
    }

    @Test
    void 끝난_게임의_결과가_되살아난다() {
        Game original = newGame();
        original.changePhase(GamePhase.NIGHT, NOW.plusSeconds(30));
        original.end(com.WhoisntCitizen_server.game.entity.Winner.CREW, List.of(3L, 4L, 5L));

        Game restored = roundTrip(original);

        assertThat(restored).usingRecursiveComparison().isEqualTo(original);
        assertThat(restored.isEnded()).isTrue();
        assertThat(restored.getWinnerIds()).containsExactly(3L, 4L, 5L);
    }

    @Test
    void 모르는_저장_형식_버전이면_거부한다() {
        GameSnapshot snapshot = GameSnapshot.from(newGame());
        GameSnapshot future = new GameSnapshot(GameSnapshot.CURRENT_SCHEMA_VERSION + 1,
                snapshot.gameId(), snapshot.roomId(), snapshot.recordStats(), snapshot.createdAt(),
                snapshot.phase(), snapshot.day(), snapshot.phaseVersion(), snapshot.phaseEndsAt(),
                snapshot.players(), snapshot.nightActions(), snapshot.lockedActors(), snapshot.skippedActors(),
                snapshot.votes(), snapshot.confirmedVoters(), snapshot.daySkippers(), snapshot.voteBanned(), snapshot.lastDeathDay(),
                snapshot.lastNightResult(), snapshot.lastExecutionResult(), snapshot.winner(), snapshot.winnerIds(),
                snapshot.endReason());

        assertThatThrownBy(future::toGame).isInstanceOf(IllegalStateException.class);
    }
}
