package com.WhoisntCitizen_server.game.result;

import com.WhoisntCitizen_server.game.entity.GameEndReason;
import com.WhoisntCitizen_server.game.entity.Winner;
import com.WhoisntCitizen_server.game.event.GameEndedEvent;
import jakarta.persistence.CollectionTable;
import jakarta.persistence.Column;
import jakarta.persistence.ElementCollection;
import jakarta.persistence.Entity;
import jakarta.persistence.EnumType;
import jakarta.persistence.Enumerated;
import jakarta.persistence.FetchType;
import jakarta.persistence.Id;
import jakarta.persistence.JoinColumn;
import jakarta.persistence.PostLoad;
import jakarta.persistence.PostPersist;
import jakarta.persistence.Table;
import jakarta.persistence.Transient;
import org.springframework.data.domain.Persistable;

import java.time.Instant;
import java.util.ArrayList;
import java.util.List;

/**
 * 끝난 게임 한 판의 결과 (game_results). gameId가 기본 키라 같은 게임은 한 번만 남는다.
 * 회원 전적 반영의 outbox 역할도 한다: statsAppliedAt이 null이면 아직 전적에 반영하지 않은 게임이다.
 * 전적에 들어가는 게임(GameEndedEvent.countsForStats)만 만든다.
 *
 * gameId를 직접 정하는 엔티티라 Persistable로 "새 엔티티"임을 알린다. 그렇지 않으면 save()가 merge로 동작해
 * 이미 있는 행을 덮어쓰고(statsAppliedAt이 null로 돌아가) 전적이 두 번 반영될 수 있다.
 */
@Entity
@Table(name = "game_results")
public class GameResult implements Persistable<String> {

    @Id
    @Column(name = "game_id")
    private String gameId;

    @Column(name = "room_id", nullable = false)
    private String roomId;

    @Enumerated(EnumType.STRING)
    @Column(nullable = false)
    private Winner winner;

    @Enumerated(EnumType.STRING)
    @Column(name = "end_reason", nullable = false)
    private GameEndReason endReason;

    @Column(name = "last_day", nullable = false)
    private int lastDay;

    @Column(name = "ended_at", nullable = false)
    private Instant endedAt;

    // 회원 전적에 반영한 시각. null이면 아직 반영 전 (GameResultService.claimForStats)
    @Column(name = "stats_applied_at")
    private Instant statsAppliedAt;

    @ElementCollection(fetch = FetchType.EAGER)
    @CollectionTable(name = "game_result_players", joinColumns = @JoinColumn(name = "game_id"))
    private List<GameResultPlayer> players = new ArrayList<>();

    @Transient
    private boolean isNew = true;

    protected GameResult() {}

    private GameResult(GameEndedEvent event, Instant endedAt) {
        this.gameId = event.gameId();
        this.roomId = event.roomId();
        this.winner = event.winner();
        this.endReason = event.endReason();
        this.lastDay = event.lastDay();
        this.endedAt = endedAt;
        this.players = new ArrayList<>(event.outcomes().stream().map(GameResultPlayer::new).toList());
    }

    /** 전적에 들어가는 게임의 종료 이벤트로 결과를 만든다. */
    public static GameResult of(GameEndedEvent event, Instant endedAt) {
        if (!event.countsForStats()) {
            throw new IllegalArgumentException("전적에 반영하지 않는 게임입니다: " + event.gameId());
        }
        return new GameResult(event, endedAt);
    }

    @PostLoad
    @PostPersist
    void markNotNew() {
        this.isNew = false;
    }

    @Override
    public String getId() { return gameId; }

    @Override
    public boolean isNew() { return isNew; }

    public String getGameId() { return gameId; }
    public String getRoomId() { return roomId; }
    public Winner getWinner() { return winner; }
    public GameEndReason getEndReason() { return endReason; }
    public int getLastDay() { return lastDay; }
    public Instant getEndedAt() { return endedAt; }
    public Instant getStatsAppliedAt() { return statsAppliedAt; }
    public List<GameResultPlayer> getPlayers() { return List.copyOf(players); }
}
