package com.WhoisntCitizen_server.jobs.domain;

import jakarta.persistence.Column;
import jakarta.persistence.Entity;
import jakarta.persistence.EnumType;
import jakarta.persistence.Enumerated;
import jakarta.persistence.GeneratedValue;
import jakarta.persistence.GenerationType;
import jakarta.persistence.Id;
import jakarta.persistence.Table;

/**
 * 게임 진행 상태. 공개 API의 gamesId는 현재 이 엔티티의 id이며 DB 테이블 이름은 rooms다.
 * Unity의 로컬 GameState와 달리 온라인 판정의 최종 상태는 서버가 소유한다.
 */
@Entity
@Table(name = "rooms")
public class RoomEntity {
    @Id
    @GeneratedValue(strategy = GenerationType.IDENTITY)
    private Long id;

    @Enumerated(EnumType.STRING)
    @Column(nullable = false)
    private RoomPhase phase;

    // 첫 낮/첫 밤은 1이다. 낮이 끝날 때만 증가하고, 행동과 공격 대상의 라운드 키에 사용한다.
    @Column(name = "night_number", nullable = false)
    private int nightNumber;

    @Enumerated(EnumType.STRING)
    @Column(name = "winner_faction")
    private Faction winnerFaction;

    protected RoomEntity() {}

    public RoomEntity(RoomPhase phase, int nightNumber) {
        this.phase = phase;
        this.nightNumber = nightNumber;
    }

    public Long getId() { return id; }
    public RoomPhase getPhase() { return phase; }
    public int getNightNumber() { return nightNumber; }
    public Faction getWinnerFaction() { return winnerFaction; }

    public void finish(Faction winner) {
        // GAME_OVER 이후에는 새 행동을 받지 않는다(RoleService의 단계 검증).
        this.winnerFaction = winner;
        this.phase = RoomPhase.GAME_OVER;
    }

    public void completeNight() {
        // 밤 판정이 끝나고 승자가 없을 때 NightResolutionService가 호출한다.
        this.phase = RoomPhase.DAY;
    }

    public void beginNextNight() {
        // 밤 번호를 먼저 올려 지난 밤과 새 밤의 행동이 서로 다른 라운드에 저장되게 한다.
        if (phase != RoomPhase.DAY) {
            throw new IllegalStateException("Only a day can advance to the next night");
        }
        this.nightNumber++;
        this.phase = RoomPhase.NIGHT;
    }
}
