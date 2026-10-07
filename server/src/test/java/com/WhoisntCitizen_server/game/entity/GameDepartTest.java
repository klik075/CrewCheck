package com.WhoisntCitizen_server.game.entity;

import com.WhoisntCitizen_server.jobs.domain.ActionCode;
import com.WhoisntCitizen_server.jobs.domain.Faction;
import com.WhoisntCitizen_server.jobs.domain.RoleDefinition;
import org.junit.jupiter.api.Test;

import java.time.Instant;
import java.util.HashMap;
import java.util.List;
import java.util.Map;

import static org.assertj.core.api.Assertions.assertThat;
import static org.assertj.core.api.Assertions.assertThatThrownBy;

/** 연결이 끊긴 플레이어 내보내기: 사망 처리와 이번 페이즈에 낸 표·행동 정리, 접속 시각 기록 */
class GameDepartTest {

    private static final RoleDefinition RAIDER =
            new RoleDefinition("PIRATE_RAIDER", "해적", Faction.PIRATE, ActionCode.SELECT_ATTACK_TARGET);
    private static final RoleDefinition PARROT =
            new RoleDefinition("PIRATE_PARROT", "앵무새", Faction.PIRATE, ActionCode.WATCH_ACTION);
    private static final RoleDefinition DOCTOR =
            new RoleDefinition("CREW_DOCTOR", "선의", Faction.CREW, ActionCode.PROTECT);
    private static final RoleDefinition DRUNK =
            new RoleDefinition("CREW_DRUNK", "주정뱅이", Faction.CREW, ActionCode.READ_CORPSE_ROLE);
    private static final RoleDefinition SAILOR =
            new RoleDefinition("CREW_SAILOR", "선원", Faction.CREW, null);

    private static final Instant NOW = Instant.parse("2026-10-01T12:00:00Z");

    // 1: 해적, 2: 앵무새, 3: 선의, 4: 주정뱅이, 5~6: 선원
    /** 참가자 1~6 모두의 마지막 요청 시각을 at으로 둔 접속 기록 */
    private static Map<Long, Instant> allSeenAt(Instant at) {
        Map<Long, Instant> lastSeen = new HashMap<>();
        for (long id = 1; id <= 6; id++) {
            lastSeen.put(id, at);
        }
        return lastSeen;
    }

    @Test
    void 참가자인지_확인한다() {
        Game game = newGame();

        assertThat(game.hasPlayer(1L)).isTrue();
        assertThat(game.hasPlayer(99L)).isFalse();
        assertThat(game.hasPlayer(null)).isFalse();
    }

    private Game newGame() {
        return new Game("room-1", List.of(
                new GamePlayer(1L, "해적", RAIDER),
                new GamePlayer(2L, "앵무새", PARROT),
                new GamePlayer(3L, "선의", DOCTOR),
                new GamePlayer(4L, "주정뱅이", DRUNK),
                new GamePlayer(5L, "선원5", SAILOR),
                new GamePlayer(6L, "선원6", SAILOR)));
    }

    @Test
    void 살아_있는_사람을_내보내면_사망_처리하고_그날을_마지막_사망일로_기록한다() {
        Game game = newGame();
        game.changePhase(GamePhase.NIGHT, NOW.plusSeconds(30));

        boolean died = game.depart(5L);

        assertThat(died).isTrue();
        assertThat(game.getPlayer(5L).isAlive()).isFalse();
        assertThat(game.getPlayer(5L).isDeparted()).isTrue();
        assertThat(game.daysWithoutDeath()).isZero();
        assertThat(game.depart(5L)).isFalse(); // 두 번 처리하지 않는다
    }

    @Test
    void 이미_죽은_사람을_내보내면_사망으로_세지_않는다() {
        Game game = newGame();
        game.changePhase(GamePhase.NIGHT, NOW.plusSeconds(30));
        game.getPlayer(5L).kill();

        assertThat(game.depart(5L)).isFalse();
        assertThat(game.daysWithoutDeath()).isEqualTo(1);
    }

    @Test
    void 떠난_사람이_대상인_살아_있는_대상_능력은_지우고_접선으로_고정된_앵무새도_다시_고를_수_있다() {
        Game game = newGame();
        game.changePhase(GamePhase.NIGHT, NOW.plusSeconds(30));
        game.recordNightAction(2L, 1L, NOW); // 앵무새 접선 → 행동 고정
        game.recordNightAction(3L, 1L, NOW); // 선의가 해적을 보호

        game.depart(1L);

        assertThat(game.getNightActions()).isEmpty();
        assertThat(game.getPlayer(2L).isContacted()).isTrue(); // 접선 자체는 되돌리지 않는다
        game.recordNightAction(2L, 5L, NOW);                    // 고정이 풀려 다시 고를 수 있다
        assertThat(game.getNightActions()).containsKey(2L);
    }

    @Test
    void 시체를_대상으로_하는_능력은_떠난_사람을_대상으로_해도_그대로_둔다() {
        Game game = newGame();
        game.changePhase(GamePhase.NIGHT, NOW.plusSeconds(30));
        game.getPlayer(6L).kill();
        game.recordNightAction(4L, 6L, NOW); // 주정뱅이가 시체(6)를 고름

        game.depart(6L);

        assertThat(game.getNightActions()).containsKey(4L);
    }

    @Test
    void 떠난_사람의_밤_행동은_지운다() {
        Game game = newGame();
        game.changePhase(GamePhase.NIGHT, NOW.plusSeconds(30));
        game.recordNightAction(1L, 5L, NOW);

        game.depart(1L);

        assertThat(game.getNightActions()).doesNotContainKey(1L);
        assertThatThrownBy(() -> game.recordNightAction(1L, 6L, NOW)).hasMessageContaining("이미 사망");
    }

    @Test
    void 마지막_요청이_기준보다_오래된_사람만_미접속으로_본다() {
        Game game = newGame();
        Map<Long, Instant> lastSeen = allSeenAt(NOW);
        lastSeen.put(1L, NOW.plusSeconds(50));
        lastSeen.put(99L, NOW); // 참가자가 아니면 무시

        List<GamePlayer> inactive = game.inactivePlayers(lastSeen, NOW.plusSeconds(10));

        assertThat(inactive).extracting(GamePlayer::getPlayerId).containsExactly(2L, 3L, 4L, 5L, 6L);
    }

    @Test
    void 이미_내보낸_사람은_다시_미접속으로_잡지_않는다() {
        Game game = newGame();
        game.changePhase(GamePhase.NIGHT, NOW.plusSeconds(30));
        game.depart(6L);

        assertThat(game.inactivePlayers(allSeenAt(NOW), NOW.plusSeconds(10)))
                .extracting(GamePlayer::getPlayerId).doesNotContain(6L);
    }

    @Test
    void 취소하면_승리_팀_없이_끝나고_이미_끝난_게임은_바꾸지_않는다() {
        Game game = newGame();
        game.changePhase(GamePhase.NIGHT, NOW.plusSeconds(30));

        game.cancel(GameEndReason.CANCELLED_ERROR);
        game.cancel(GameEndReason.CANCELLED_NO_DEATHS);

        assertThat(game.isEnded()).isTrue();
        assertThat(game.isCancelled()).isTrue();
        assertThat(game.getWinner()).isNull();
        assertThat(game.getEndReason()).isEqualTo(GameEndReason.CANCELLED_ERROR);
    }
}
