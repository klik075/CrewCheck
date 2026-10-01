package com.WhoisntCitizen_server.game.entity;

import com.WhoisntCitizen_server.common.exception.GameRuleException;
import com.WhoisntCitizen_server.jobs.domain.ActionCode;
import com.WhoisntCitizen_server.jobs.domain.Faction;
import com.WhoisntCitizen_server.night.entity.NightResult;
import com.WhoisntCitizen_server.vote.entity.ExecutionResult;
import lombok.Getter;

import java.time.Instant;
import java.util.Collections;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;
import java.util.UUID;

/**
 * 게임 한 판의 상태(Aggregate Root).
 * 상태 변경은 모두 이 클래스의 메서드를 통해서만 일어나며, 규칙 위반은 GameRuleException으로 거부한다.
 * 동시성 제어는 Service 계층에서 Game 인스턴스 단위로 synchronized 한다.
 */
@Getter
public class Game {

    private final String gameId;
    private final String roomId;
    // 전적(User.playCount/winCount)에 반영할 게임인지. 방에서 시작한 실제 게임만 true, 개발용 테스트 게임은 false
    private final boolean recordStats;
    private final Map<Long, GamePlayer> players; // 입장 순서 유지
    private final Instant createdAt;

    private GamePhase phase;
    private int day;              // 첫 밤이 1일차
    private long phaseVersion;    // 페이즈가 바뀔 때마다 증가. 오래된 타이머 작업을 무시하는 데 사용
    private Instant phaseEndsAt;

    private final Map<Long, Long> nightActions = new LinkedHashMap<>(); // actorId -> targetId
    private final Map<Long, Long> votes = new LinkedHashMap<>();        // voterId -> targetId

    private NightResult lastNightResult;
    private ExecutionResult lastExecutionResult;
    private Faction winner;

    public Game(String roomId, List<GamePlayer> players) {
        this(roomId, players, false);
    }

    public Game(String roomId, List<GamePlayer> players, boolean recordStats) {
        this.gameId = UUID.randomUUID().toString();
        this.roomId = roomId;
        this.recordStats = recordStats;
        this.players = new LinkedHashMap<>();
        for (GamePlayer p : players) {
            this.players.put(p.getPlayerId(), p);
        }
        this.createdAt = Instant.now();
        // start 전
        this.phase = null;
        this.day = 0;
    }

    // ---------- 페이즈 전이 ----------

    public void changePhase(GamePhase next, Instant endsAt) {
        if (phase == GamePhase.ENDED) {
            throw new GameRuleException("이미 종료된 게임입니다.");
        }
        if (next == GamePhase.NIGHT) {
            day++;
            nightActions.clear();
        }
        if (next == GamePhase.VOTE) {
            votes.clear();
        }
        this.phase = next;
        this.phaseEndsAt = endsAt;
        this.phaseVersion++;
    }

    public void end(Faction winner) {
        this.winner = winner;
        this.phase = GamePhase.ENDED;
        this.phaseEndsAt = null;
        this.phaseVersion++;
    }

    // ---------- 3. 밤 능력 ----------

    public void recordNightAction(Long actorId, Long targetId) {
        requirePhase(GamePhase.NIGHT);
        GamePlayer actor = getAlivePlayer(actorId, "행동하는 플레이어");
        GamePlayer target = getAlivePlayer(targetId, "대상 플레이어");

        // 직업 이름이 아니라 DB에 연결된 ActionCode 규칙으로 검증한다.
        if (!actor.getRole().hasNightAction()) {
            throw new GameRuleException("밤에 사용할 능력이 없는 직업입니다.");
        }
        ActionCode code = actor.getRole().actionCode();
        switch (code) {
            // 현재 게임 로직이 처리하는 밤 능력: 공격(해적) / 조사(선장) / 보호(선의)
            case SELECT_ATTACK_TARGET, INVESTIGATE_FACTION, PROTECT -> { }
            default -> throw new GameRuleException("아직 지원하지 않는 능력입니다: " + code);
        }
        if (!code.allowsSelfTarget() && actor.getPlayerId().equals(target.getPlayerId())) {
            throw new GameRuleException("자신을 대상으로 할 수 없는 능력입니다.");
        }
        nightActions.put(actorId, targetId);
    }

    public boolean allNightActionsSubmitted() {
        long required = players.values().stream()
                .filter(p -> p.isAlive() && p.getRole().hasNightAction())
                .count();
        return nightActions.size() >= required;
    }

    // ---------- 6. 투표 ----------

    public void recordVote(Long voterId, Long targetId) {
        requirePhase(GamePhase.VOTE);
        getAlivePlayer(voterId, "투표하는 플레이어");
        getAlivePlayer(targetId, "투표 대상");
        votes.put(voterId, targetId); // 재투표 시 덮어쓰기
    }

    public boolean allVotesSubmitted() {
        return votes.size() >= aliveCount();
    }

    // ---------- 조회/헬퍼 ----------

    public void requirePhase(GamePhase expected) {
        if (phase != expected) {
            throw new GameRuleException("현재 페이즈(" + phase + ")에서는 할 수 없는 요청입니다. 필요 페이즈: " + expected);
        }
    }

    public GamePlayer getPlayer(Long playerId) {
        GamePlayer p = players.get(playerId);
        if (p == null) {
            throw new GameRuleException("이 게임에 참가하지 않은 플레이어입니다: " + playerId);
        }
        return p;
    }

    private GamePlayer getAlivePlayer(Long playerId, String label) {
        GamePlayer p = getPlayer(playerId);
        if (!p.isAlive()) {
            throw new GameRuleException(label + "가 이미 사망했습니다: " + playerId);
        }
        return p;
    }

    public long aliveCount() {
        return players.values().stream().filter(GamePlayer::isAlive).count();
    }

    public List<GamePlayer> getPlayers() {
        return List.copyOf(players.values());
    }

    public Map<Long, Long> getNightActions() {
        return Collections.unmodifiableMap(nightActions);
    }

    public Map<Long, Long> getVotes() {
        return Collections.unmodifiableMap(votes);
    }

    public void setLastNightResult(NightResult result) { this.lastNightResult = result; }
    public void setLastExecutionResult(ExecutionResult result) { this.lastExecutionResult = result; }

    public boolean isEnded() { return phase == GamePhase.ENDED; }
}
