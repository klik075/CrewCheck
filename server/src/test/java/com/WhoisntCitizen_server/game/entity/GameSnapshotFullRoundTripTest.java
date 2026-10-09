package com.WhoisntCitizen_server.game.entity;

import com.WhoisntCitizen_server.game.repository.snapshot.GameSnapshot;
import com.WhoisntCitizen_server.jobs.domain.ActionCode;
import com.WhoisntCitizen_server.jobs.domain.Faction;
import com.WhoisntCitizen_server.night.entity.NightAction;
import com.WhoisntCitizen_server.night.entity.NightResult;
import com.WhoisntCitizen_server.night.entity.PrivateReport;
import com.WhoisntCitizen_server.support.TestRoles;
import com.WhoisntCitizen_server.vote.entity.ExecutionResult;
import org.junit.jupiter.api.Test;
import tools.jackson.databind.json.JsonMapper;

import java.lang.reflect.Field;
import java.lang.reflect.Modifier;
import java.time.Instant;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;

import static org.assertj.core.api.Assertions.assertThat;

/**
 * 1-3: 저장할 때 빠지는 필드가 없는지 잡는 테스트.
 *
 * 두 단계로 막는다.
 *  1) 픽스처 점검: fullGame()이 Game·GamePlayer의 "모든 필드"를 기본값이 아닌 값으로 채웠는지 리플렉션으로 확인한다.
 *     Game이나 GamePlayer에 필드를 추가하면, 그 필드를 여기서 채우기 전까지 이 테스트가 실패한다.
 *     (채우려면 restoreState에 파라미터를 추가해야 하고, 그러면 GameSnapshotMapper·스냅샷 record도 고치게 된다)
 *  2) 왕복 비교: 모든 필드가 채워진 게임을 스냅샷 → JSON → 스냅샷 → Game으로 돌린 뒤 원본과 필드 단위로 비교한다.
 *     매퍼에서 값을 빠뜨리거나 순서를 잃으면 실패한다.
 *
 * 규칙 검사를 거치지 않는 restoreState로 상태를 직접 채운다. 그래서 실제 게임에서는 나올 수 없는 조합(끝난 게임에 밤 행동이 남아 있는 등)도
 * 섞여 있다. 목적은 "규칙에 맞는 게임"이 아니라 "모든 칸이 차 있는 게임"이다.
 * restoreState와 PACKAGE getter를 쓰기 위해 Game과 같은 패키지에 둔다.
 */
class GameSnapshotFullRoundTripTest {

    private static final Instant T = Instant.parse("2026-10-01T12:00:00Z");

    private final JsonMapper jsonMapper = JsonMapper.builder().build();

    /** 모든 필드가 기본값과 다른 플레이어. 원숭이(실제) / 주정뱅이(보이는 직업)로 role과 shownRole을 다르게 둔다. */
    private static GamePlayer fullPlayer() {
        GamePlayer p = new GamePlayer(9L, "원숭이", TestRoles.MONKEY, TestRoles.DRUNK);
        Map<ActionCode, Integer> usedCounts = new LinkedHashMap<>();
        usedCounts.put(ActionCode.READ_CORPSE_ROLE, 1);
        usedCounts.put(ActionCode.PROTECT, 2);
        Map<Long, Faction> fakeFactions = new LinkedHashMap<>();
        fakeFactions.put(5L, Faction.PIRATE);
        fakeFactions.put(2L, Faction.CREW);
        p.restoreState(Team.SIREN, false, T.minusSeconds(50), DeathCause.KRAKEN,
                usedCounts, 2,
                3, 5L, 1,
                List.of(5L, 2L),                                   // 크라켄 표식 (순서 의미 있음, 정렬과 다르게)
                fakeFactions,
                Map.of(2L, TestRoles.CAPTAIN),
                T.minusSeconds(300), T.minusSeconds(200), true);
        return p;
    }

    /** 모든 필드가 기본값과 다른 게임. 입장·제출·투표 순서를 id 정렬 순서와 다르게 둬서 순서 손실도 잡는다. */
    private static Game fullGame() {
        List<GamePlayer> players = List.of(
                fullPlayer(),
                new GamePlayer(5L, "해적", TestRoles.RAIDER),
                new GamePlayer(2L, "선장", TestRoles.CAPTAIN));
        Game game = new Game("game-full", "7", true, T.minusSeconds(3600), players);

        Map<Long, Long> votes = new LinkedHashMap<>();
        votes.put(5L, 2L);
        votes.put(2L, 5L);

        Map<Long, List<PrivateReport>> reports = new LinkedHashMap<>();
        reports.put(2L, List.of(PrivateReport.corpseRole(9L, TestRoles.MONKEY), PrivateReport.blocked()));
        NightResult nightResult = new NightResult(3, 9L, true,
                List.of(new NightResult.Death(9L, DeathCause.KRAKEN)), reports);
        Map<Long, Integer> voteCounts = new LinkedHashMap<>();
        voteCounts.put(5L, 1);
        voteCounts.put(2L, 1);
        ExecutionResult executionResult = new ExecutionResult(3, 5L, true, voteCounts);

        game.restoreState(GamePhase.ENDED, 3, 17L, T.plusSeconds(30),
                List.of(new NightAction(5L, ActionCode.SELECT_ATTACK_TARGET, 2L),
                        new NightAction(2L, ActionCode.READ_CORPSE_ROLE, 9L)),
                List.of(5L), List.of(2L),
                votes,
                List.of(5L),                                       // confirmedVoters
                List.of(9L),                                       // daySkippers
                List.of(2L, 5L),                                   // voteBanned
                2, nightResult, executionResult,
                Winner.PIRATE, List.of(5L), GameEndReason.WIN);
        return game;
    }

    // ---------- 1) 픽스처가 정말 모든 필드를 채웠는지 ----------

    @Test
    void 픽스처는_Game의_모든_필드를_기본값과_다르게_채운다() throws Exception {
        assertEveryFieldDiffers(fullGame(), new Game("기본", List.of()));
    }

    @Test
    void 픽스처는_GamePlayer의_모든_필드를_기본값과_다르게_채운다() throws Exception {
        assertEveryFieldDiffers(fullPlayer(), new GamePlayer(99L, "기본", TestRoles.SAILOR));
    }

    /**
     * full의 각 필드가 fresh(막 만든 객체)의 같은 필드와 다른지 확인한다.
     * 같다면 그 필드는 기본값 그대로라서, 저장에서 빠져도 왕복 테스트가 알아채지 못한다.
     */
    private static void assertEveryFieldDiffers(Object full, Object fresh) throws IllegalAccessException {
        for (Field field : full.getClass().getDeclaredFields()) {
            if (Modifier.isStatic(field.getModifiers()) || field.isSynthetic()) {
                continue;
            }
            field.setAccessible(true);
            assertThat(field.get(full))
                    .as("%s.%s 이(가) 기본값 그대로다. 새 필드라면 fullGame()/fullPlayer()에서 채우고, "
                                    + "restoreState·스냅샷 record·GameSnapshotMapper에도 추가하자",
                            full.getClass().getSimpleName(), field.getName())
                    .isNotEqualTo(field.get(fresh));
        }
    }

    // ---------- 2) 모든 필드가 찬 게임의 왕복 ----------

    @Test
    void 모든_필드가_찬_게임이_JSON을_거쳐_그대로_되살아난다() {
        Game original = fullGame();
        GameSnapshot snapshot = GameSnapshot.from(original);

        String json = jsonMapper.writeValueAsString(snapshot);
        GameSnapshot readBack = jsonMapper.readValue(json, GameSnapshot.class);
        Game restored = readBack.toGame();

        // 스냅샷이 JSON을 거쳐도 같다 (List 순서까지 비교됨)
        assertThat(readBack).isEqualTo(snapshot);
        // 되살린 Game의 모든 필드(숨은 필드 포함)가 원본과 같다
        assertThat(restored).usingRecursiveComparison().isEqualTo(original);
        // 되살린 Game을 다시 저장해도 같은 스냅샷이 나온다 (저장 → 조회 → 저장 반복에도 값이 변하지 않음)
        assertThat(GameSnapshot.from(restored)).isEqualTo(snapshot);
    }

    @Test
    void 순서가_의미_있는_값은_순서까지_되살아난다() {
        Game restored = jsonRoundTrip(fullGame());

        assertThat(restored.getPlayers()).extracting(GamePlayer::getPlayerId).containsExactly(9L, 5L, 2L);
        assertThat(restored.getNightActions().keySet()).containsExactly(5L, 2L);
        assertThat(restored.getVotes().keySet()).containsExactly(5L, 2L);
        assertThat(restored.getPlayer(9L).getKrakenMarks()).containsExactly(5L, 2L);
        assertThat(restored.getLastExecutionResult().voteCounts().keySet()).containsExactly(5L, 2L);
        assertThat(restored.getLastNightResult().reportsFor(2L)).hasSize(2);
    }

    @Test
    void 숨은_상태가_규칙_메서드에도_그대로_반영된다() {
        Game restored = jsonRoundTrip(fullGame());
        GamePlayer monkey = restored.getPlayer(9L);

        assertThat(restored.isVoteBanned(2L)).isTrue();
        assertThat(restored.hasConfirmedVote(5L)).isTrue();
        assertThat(restored.hasConfirmedVote(2L)).isFalse();
        assertThat(monkey.getRole()).isEqualTo(TestRoles.MONKEY);
        assertThat(monkey.getShownRole()).isEqualTo(TestRoles.DRUNK);
        assertThat(monkey.getTeam()).isEqualTo(Team.SIREN);
        assertThat(monkey.isDeparted()).isTrue();
        // 원숭이 가짜 결과는 저장된 값을 쓴다 (draw가 다시 불리지 않음)
        assertThat(monkey.fakeFactionOf(5L, () -> Faction.CREW)).isEqualTo(Faction.PIRATE);
        assertThat(monkey.fakeCorpseRoleOf(2L, () -> TestRoles.SAILOR)).isEqualTo(TestRoles.CAPTAIN);
    }

    private Game jsonRoundTrip(Game game) {
        String json = jsonMapper.writeValueAsString(GameSnapshot.from(game));
        return jsonMapper.readValue(json, GameSnapshot.class).toGame();
    }
}
