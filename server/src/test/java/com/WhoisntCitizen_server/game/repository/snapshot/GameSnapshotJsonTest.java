package com.WhoisntCitizen_server.game.repository.snapshot;

import com.WhoisntCitizen_server.game.entity.DeathCause;
import com.WhoisntCitizen_server.game.entity.GameEndReason;
import com.WhoisntCitizen_server.game.entity.GamePhase;
import com.WhoisntCitizen_server.game.entity.Team;
import com.WhoisntCitizen_server.game.entity.Winner;
import com.WhoisntCitizen_server.jobs.domain.ActionCode;
import com.WhoisntCitizen_server.jobs.domain.Faction;
import com.WhoisntCitizen_server.night.entity.NightAction;
import com.WhoisntCitizen_server.night.entity.NightResult;
import com.WhoisntCitizen_server.night.entity.PrivateReport;
import com.WhoisntCitizen_server.support.TestRoles;
import com.WhoisntCitizen_server.vote.entity.ExecutionResult;
import org.junit.jupiter.api.Test;
import tools.jackson.databind.json.JsonMapper;

import java.time.Instant;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;

import static org.assertj.core.api.Assertions.assertThat;

/**
 * 1-1: 저장 형식(GameSnapshot) 자체가 JSON으로 손실 없이 왕복되는지 확인한다. (Game 변환은 1-2/1-3에서)
 * Map의 Long 키, Instant, enum, 중첩 record(밤 결과·개인 결과·처형 결과)가 모두 되살아나야 한다.
 */
class GameSnapshotJsonTest {

    private static final Instant NOW = Instant.parse("2026-10-01T12:00:00Z");

    private final JsonMapper jsonMapper = JsonMapper.builder().build();

    private static PlayerSnapshot player(long id, String nickname, RoleSnapshot role, RoleSnapshot shownRole) {
        return new PlayerSnapshot(id, nickname, role, shownRole, Team.CREW, true, null, null,
                Map.of(), null, null, null, null, List.of(), Map.of(), Map.of(), null, null, false);
    }

    private static GameSnapshot fullSnapshot() {
        RoleSnapshot raider = RoleSnapshot.from(TestRoles.RAIDER);
        RoleSnapshot captain = RoleSnapshot.from(TestRoles.CAPTAIN);
        RoleSnapshot monkey = RoleSnapshot.from(TestRoles.MONKEY);
        RoleSnapshot drunk = RoleSnapshot.from(TestRoles.DRUNK);
        RoleSnapshot parrot = RoleSnapshot.from(TestRoles.PARROT);

        PlayerSnapshot deadMonkey = new PlayerSnapshot(3L, "원숭이", monkey, drunk, Team.CREW, false,
                NOW.minusSeconds(100), DeathCause.ATTACK,
                Map.of(ActionCode.READ_CORPSE_ROLE, 1), 2, 1, 4L, null,
                List.of(5L, 4L), Map.of(1L, Faction.PIRATE), Map.of(4L, captain),
                null, null, false);
        PlayerSnapshot contactedParrot = new PlayerSnapshot(2L, "앵무새", parrot, parrot, Team.PIRATE, true,
                null, null, Map.of(), null, null, null, null, List.of(), Map.of(), Map.of(),
                NOW.minusSeconds(30), null, false);
        PlayerSnapshot departed = new PlayerSnapshot(4L, "선장", captain, captain, Team.CREW, false,
                NOW.minusSeconds(10), DeathCause.DISCONNECT, Map.of(), null, null, null, null,
                List.of(), Map.of(), Map.of(), null, null, true);

        Map<Long, List<PrivateReport>> reports = new LinkedHashMap<>();
        reports.put(2L, List.of(PrivateReport.actions(1L,
                List.of(new PrivateReport.ObservedAction(ActionCode.SELECT_ATTACK_TARGET, 3L)))));
        reports.put(3L, List.of(PrivateReport.corpseRole(4L, TestRoles.CAPTAIN), PrivateReport.blocked()));
        NightResult nightResult = new NightResult(2, 3L, false,
                List.of(new NightResult.Death(3L, DeathCause.ATTACK)), reports);

        Map<Long, Integer> voteCounts = new LinkedHashMap<>();
        voteCounts.put(1L, 2);
        voteCounts.put(5L, 1);
        ExecutionResult executionResult = new ExecutionResult(2, null, false, voteCounts);

        return new GameSnapshot(GameSnapshot.CURRENT_SCHEMA_VERSION,
                "game-1", "7", true, NOW.minusSeconds(600),
                GamePhase.VOTE, 2, 9L, NOW.plusSeconds(30),
                List.of(player(1L, "해적", raider, raider), contactedParrot, deadMonkey, departed),
                List.of(new NightAction(1L, ActionCode.SELECT_ATTACK_TARGET, 3L),
                        new NightAction(9L, ActionCode.KRAKEN_STRIKE, null)),
                List.of(2L), List.of(5L, 1L),
                List.of(new GameSnapshot.VoteEntry(2L, 1L), new GameSnapshot.VoteEntry(1L, 5L)),
                List.of(2L), List.of(), List.of(5L),
                2,
                nightResult, executionResult,
                null, List.of(), null);
    }

    @Test
    void JSON으로_바꿨다가_되돌리면_같은_스냅샷이다() {
        GameSnapshot original = fullSnapshot();

        String json = jsonMapper.writeValueAsString(original);
        GameSnapshot restored = jsonMapper.readValue(json, GameSnapshot.class);

        assertThat(restored).isEqualTo(original);
        assertThat(restored.players().get(2).usedCounts()).containsEntry(ActionCode.READ_CORPSE_ROLE, 1);
        assertThat(restored.players().get(2).fakeCorpseRoles().get(4L).toRole()).isEqualTo(TestRoles.CAPTAIN);
        assertThat(restored.lastNightResult().reportsFor(3L)).hasSize(2);
    }

    @Test
    void 끝난_게임도_왕복된다() {
        GameSnapshot ended = new GameSnapshot(GameSnapshot.CURRENT_SCHEMA_VERSION,
                "game-2", "7", false, NOW, GamePhase.ENDED, 3, 12L, null,
                List.of(), List.of(), List.of(), List.of(), List.of(), List.of(), List.of(), List.of(),
                3, null, null, Winner.CREW, List.of(2L, 3L), GameEndReason.WIN);

        String json = jsonMapper.writeValueAsString(ended);

        assertThat(jsonMapper.readValue(json, GameSnapshot.class)).isEqualTo(ended);
    }

    @Test
    void 저장_형식에_버전이_들어가고_직업은_판단_메서드_없이_네_값만_저장된다() {
        String json = jsonMapper.writeValueAsString(fullSnapshot());

        assertThat(json).contains("\"schemaVersion\":1");
        assertThat(json).contains("\"code\":\"PIRATE_RAIDER\"");
        assertThat(json).doesNotContain("\"pirate\"", "\"raider\"", "\"monkey\"");
    }

    @Test
    void 집합은_정렬되고_null_목록은_빈_목록이_된다() {
        GameSnapshot snapshot = new GameSnapshot(GameSnapshot.CURRENT_SCHEMA_VERSION,
                "game-3", "7", true, NOW, GamePhase.NIGHT, 1, 1L, NOW,
                null, null, List.of(3L, 1L, 2L), null, null, List.of(9L, 4L), List.of(7L, 2L), null,
                0, null, null, null, null, null);

        assertThat(snapshot.lockedActors()).containsExactly(1L, 2L, 3L);
        assertThat(snapshot.confirmedVoters()).containsExactly(4L, 9L);
        assertThat(snapshot.daySkippers()).containsExactly(2L, 7L);
        assertThat(snapshot.players()).isEmpty();
        assertThat(snapshot.votes()).isEmpty();
        assertThat(snapshot.winnerIds()).isEmpty();
    }
}
