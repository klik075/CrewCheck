package com.WhoisntCitizen_server.jobs.domain;

import jakarta.persistence.Column;
import jakarta.persistence.Entity;
import jakarta.persistence.GeneratedValue;
import jakarta.persistence.GenerationType;
import jakarta.persistence.Id;
import jakarta.persistence.Table;
import jakarta.persistence.UniqueConstraint;
import java.util.UUID;

/** 낮/밤에 접수한 행동. 밤 행동의 효과는 밤 종료 판정기가 적용한다. */
@Entity
@Table(name = "room_actions", uniqueConstraints = {
    // request_id는 통신 재시도용, 두 번째 제약은 단계·라운드·능력별 중복 방지용이다.
    // 해적 공격 대상은 밤 동안 바꿀 수 있으므로 이 테이블 대신 pirate_attack_selections에 저장한다.
    @UniqueConstraint(columnNames = {"room_id", "actor_player_id", "request_id"}),
    @UniqueConstraint(columnNames = {"room_id", "actor_player_id", "phase", "round_number", "action_code"})
})
public class RoomActionEntity {
    @Id
    @GeneratedValue(strategy = GenerationType.IDENTITY)
    private Long id;

    @Column(name = "room_id", nullable = false)
    private Long roomId;

    @Column(name = "actor_player_id", nullable = false)
    private Long actorPlayerId;

    @jakarta.persistence.Enumerated(jakarta.persistence.EnumType.STRING)
    @Column(nullable = false)
    private RoomPhase phase;

    @Column(name = "round_number", nullable = false)
    private int roundNumber;

    @Column(name = "request_id", nullable = false)
    private UUID requestId;

    @Column(name = "action_code", nullable = false)
    private String actionCode;

    @Column(name = "target_player_id", nullable = false)
    private Long targetPlayerId;

    @Column(nullable = false)
    private String status;

    protected RoomActionEntity() {}

    public RoomActionEntity(Long roomId, Long actorPlayerId, RoomPhase phase, int roundNumber,
                            UUID requestId, String actionCode, Long targetPlayerId) {
        this.roomId = roomId;
        this.actorPlayerId = actorPlayerId;
        this.phase = phase;
        this.roundNumber = roundNumber;
        this.requestId = requestId;
        this.actionCode = actionCode;
        this.targetPlayerId = targetPlayerId;
        this.status = "SUBMITTED";
    }

    public Long getId() { return id; }
    public Long getRoomId() { return roomId; }
    public Long getActorPlayerId() { return actorPlayerId; }
    public RoomPhase getPhase() { return phase; }
    public int getRoundNumber() { return roundNumber; }
    public String getActionCode() { return actionCode; }
    public Long getTargetPlayerId() { return targetPlayerId; }
    public String getStatus() { return status; }

    public void resolve() { this.status = "RESOLVED"; }
}
