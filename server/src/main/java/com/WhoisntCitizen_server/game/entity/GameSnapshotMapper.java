package com.WhoisntCitizen_server.game.entity;

import com.WhoisntCitizen_server.game.repository.snapshot.GameSnapshot;
import com.WhoisntCitizen_server.game.repository.snapshot.PlayerSnapshot;
import com.WhoisntCitizen_server.game.repository.snapshot.RoleSnapshot;
import com.WhoisntCitizen_server.jobs.domain.RoleDefinition;

import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;

/**
 * Game ↔ GameSnapshot 변환. 저장소(GameRepository 구현체)만 쓴다.
 *
 * Game과 같은 패키지에 두는 이유: 게임 규칙에 쓰이지 않는 내부 상태(밤 행동 확정자, 투표 완료자, 능력 사용 기록 등)를
 * 밖으로 열지 않고(PACKAGE getter), 저장된 상태를 규칙 검사 없이 채우는 복원 메서드(restoreState)도
 * 이 패키지 안에서만 부를 수 있게 하기 위해서다. 서비스 코드는 이 내부 상태에 손댈 수 없다.
 *
 * Game·GamePlayer에 필드를 추가하면 GameSnapshot·PlayerSnapshot과 이 클래스를 함께 고친다.
 * (빠뜨리면 왕복 테스트가 실패한다)
 */
public final class GameSnapshotMapper {

    private GameSnapshotMapper() {
    }

    // ---------- Game → 스냅샷 (저장) ----------

    public static GameSnapshot toSnapshot(Game game) {
        List<GameSnapshot.VoteEntry> votes = game.getVotes().entrySet().stream()
                .map(e -> new GameSnapshot.VoteEntry(e.getKey(), e.getValue()))
                .toList();
        return new GameSnapshot(
                GameSnapshot.CURRENT_SCHEMA_VERSION,
                game.getGameId(),
                game.getRoomId(),
                game.isRecordStats(),
                game.getCreatedAt(),
                game.getPhase(),
                game.getDay(),
                game.getPhaseVersion(),
                game.getPhaseEndsAt(),
                game.getPlayers().stream().map(GameSnapshotMapper::toSnapshot).toList(),
                List.copyOf(game.getNightActions().values()),
                List.copyOf(game.getLockedActors()),
                List.copyOf(game.getSkippedActors()),
                votes,
                List.copyOf(game.getConfirmedVoters()),
                List.copyOf(game.getDaySkippers()),
                List.copyOf(game.getVoteBanned()),
                game.getLastDeathDay(),
                game.getLastNightResult(),
                game.getLastExecutionResult(),
                game.getWinner(),
                game.getWinnerIds(),
                game.getEndReason());
    }

    private static PlayerSnapshot toSnapshot(GamePlayer p) {
        Map<Long, RoleSnapshot> fakeCorpseRoles = new LinkedHashMap<>();
        p.getFakeCorpseRoles().forEach((targetId, role) -> fakeCorpseRoles.put(targetId, RoleSnapshot.from(role)));
        return new PlayerSnapshot(
                p.getPlayerId(),
                p.getNickname(),
                RoleSnapshot.from(p.getRole()),
                RoleSnapshot.from(p.getShownRole()),
                p.getTeam(),
                p.isAlive(),
                p.getDiedAt(),
                p.getDeathCause(),
                p.getUsedCounts(),
                p.getLastSelfProtectDay(),
                p.getLastVoteBanDay(),
                p.getLastVoteBanTargetId(),
                p.getLastSeduceSuccessDay(),
                p.getKrakenMarks(),
                p.getFakeFactions(),
                fakeCorpseRoles,
                p.getContactedAt(),
                p.getSeducedAt(),
                p.isDeparted());
    }

    // ---------- 스냅샷 → Game (조회) ----------

    public static Game toGame(GameSnapshot s) {
        if (s.schemaVersion() != GameSnapshot.CURRENT_SCHEMA_VERSION) {
            throw new IllegalStateException("지원하지 않는 게임 저장 형식 버전입니다: " + s.schemaVersion()
                    + " (gameId=" + s.gameId() + ", 지원 버전=" + GameSnapshot.CURRENT_SCHEMA_VERSION + ")");
        }
        List<GamePlayer> players = s.players().stream().map(GameSnapshotMapper::toPlayer).toList();
        Game game = new Game(s.gameId(), s.roomId(), s.recordStats(), s.createdAt(), players);

        Map<Long, Long> votes = new LinkedHashMap<>();   // 투표 순서 유지
        s.votes().forEach(v -> votes.put(v.voterId(), v.targetId()));

        game.restoreState(s.phase(), s.day(), s.phaseVersion(), s.phaseEndsAt(),
                s.nightActions(), s.lockedActors(), s.skippedActors(),
                votes, s.confirmedVoters(), s.daySkippers(), s.voteBanned(),
                s.lastDeathDay(), s.lastNightResult(), s.lastExecutionResult(),
                s.winner(), s.winnerIds(), s.endReason());
        return game;
    }

    private static GamePlayer toPlayer(PlayerSnapshot s) {
        GamePlayer player = new GamePlayer(s.playerId(), s.nickname(), s.role().toRole(), s.shownRole().toRole());
        Map<Long, RoleDefinition> fakeCorpseRoles = new LinkedHashMap<>();
        s.fakeCorpseRoles().forEach((targetId, role) -> fakeCorpseRoles.put(targetId, role.toRole()));
        player.restoreState(s.team(), s.alive(), s.diedAt(), s.deathCause(),
                s.usedCounts(), s.lastSelfProtectDay(),
                s.lastVoteBanDay(), s.lastVoteBanTargetId(), s.lastSeduceSuccessDay(),
                s.krakenMarks(), s.fakeFactions(), fakeCorpseRoles,
                s.contactedAt(), s.seducedAt(), s.departed());
        return player;
    }
}
