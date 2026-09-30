package com.WhoisntCitizen_server.jobs.domain;

import jakarta.persistence.Column;
import jakarta.persistence.Entity;
import jakarta.persistence.GeneratedValue;
import jakarta.persistence.GenerationType;
import jakarta.persistence.Id;
import jakarta.persistence.Table;
import jakarta.persistence.UniqueConstraint;

/** 방 참가자 한 명의 배정 직업, 보여 줄 직업, 생존 상태. 능력 사용 횟수는 행동 기록에서 센다. */
@Entity
@Table(name = "room_players", uniqueConstraints = {
    @UniqueConstraint(columnNames = {"room_id", "user_id"}),
    @UniqueConstraint(columnNames = {"room_id", "player_id"})
})
public class RoomPlayerEntity {
    @Id
    @GeneratedValue(strategy = GenerationType.IDENTITY)
    private Long id;

    @Column(name = "room_id", nullable = false)
    private Long roomId;

    @Column(name = "user_id", nullable = false)
    private Long userId;

    // userId는 계정 식별자, playerId는 방 안의 번호다. targetPlayerId에는 후자를 사용한다.
    @Column(name = "player_id", nullable = false)
    private Long playerId;

    // 입장 직후 SETUP 단계에서는 아직 배정 전이므로 null일 수 있다.
    @Column(name = "role_code")
    private String roleCode;

    // 원숭이에게만 실제 직업과 다른 선원 직업을 보여 준다.
    @Column(name = "shown_role_code")
    private String shownRoleCode;

    @Column(nullable = false)
    private boolean alive;

    protected RoomPlayerEntity() {}

    public RoomPlayerEntity(Long roomId, Long userId, Long playerId,
                            String roleCode, boolean alive) {
        this.roomId = roomId;
        this.userId = userId;
        this.playerId = playerId;
        this.alive = alive;
        assignRole(roleCode);
    }

    public Long getRoomId() { return roomId; }
    public Long getUserId() { return userId; }
    public Long getPlayerId() { return playerId; }
    public String getRoleCode() { return roleCode; }
    public String getShownRoleCode() { return shownRoleCode == null ? roleCode : shownRoleCode; }
    public boolean isAlive() { return alive; }

    public void assignRole(String roleCode) {
        // 배정 서비스는 호출 전 활성 직업인지 검증하고, SETUP에서 참가자 전원을 함께 배정해야 한다.
        // 원숭이는 실제 CREW_MONKEY를 유지하되 비공개 행동만 있는 직업으로 위장한다.
        // playerId로 위장 직업을 결정하므로 배정 후 playerId를 바꾸면 안 된다.
        this.roleCode = roleCode;
        String[] disguises = {"CREW_CAPTAIN", "CREW_DOCTOR", "CREW_LOOKOUT", "CREW_BOATSWAIN"};
        this.shownRoleCode = "CREW_MONKEY".equals(roleCode)
            ? disguises[Math.floorMod(playerId.intValue(), disguises.length)] : null;
    }

    public void kill() {
        this.alive = false;
    }
}
