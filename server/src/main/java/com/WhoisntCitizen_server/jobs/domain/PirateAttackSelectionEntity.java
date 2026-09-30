package com.WhoisntCitizen_server.jobs.domain;

import jakarta.persistence.Column;
import jakarta.persistence.Entity;
import jakarta.persistence.GeneratedValue;
import jakarta.persistence.GenerationType;
import jakarta.persistence.Id;
import jakarta.persistence.Table;
import jakarta.persistence.UniqueConstraint;
import java.util.UUID;

/** 해적이 밤에 고른 공유 공격 대상의 변경 이력. 이번 밤의 마지막 행이 최종 선택이다. */
@Entity
@Table(name = "pirate_attack_selections", uniqueConstraints = {
    @UniqueConstraint(columnNames = {"room_id", "actor_player_id", "request_id"})
})
public class PirateAttackSelectionEntity {
    @Id
    @GeneratedValue(strategy = GenerationType.IDENTITY)
    private Long id;

    @Column(name = "room_id", nullable = false)
    private Long roomId;

    @Column(name = "night_number", nullable = false)
    private int nightNumber;

    @Column(name = "actor_player_id", nullable = false)
    private Long actorPlayerId;

    @Column(name = "request_id", nullable = false)
    private UUID requestId;

    @Column(name = "target_player_id", nullable = false)
    private Long targetPlayerId;

    protected PirateAttackSelectionEntity() {}

    public PirateAttackSelectionEntity(Long roomId, int nightNumber, Long actorPlayerId,
                                       UUID requestId, Long targetPlayerId) {
        this.roomId = roomId;
        this.nightNumber = nightNumber;
        this.actorPlayerId = actorPlayerId;
        this.requestId = requestId;
        this.targetPlayerId = targetPlayerId;
    }

    public Long getId() { return id; }
    public Long getRoomId() { return roomId; }
    public int getNightNumber() { return nightNumber; }
    public Long getActorPlayerId() { return actorPlayerId; }
    public Long getTargetPlayerId() { return targetPlayerId; }
}
