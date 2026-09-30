package com.WhoisntCitizen_server.jobs.domain;

import jakarta.persistence.Column;
import jakarta.persistence.Entity;
import jakarta.persistence.EnumType;
import jakarta.persistence.Enumerated;
import jakarta.persistence.Id;
import jakarta.persistence.Table;

/** 모든 플레이어에게 공개되는 직업 정의. 실제 배정 결과는 RoomPlayerEntity에 저장한다. */
@Entity
@Table(name = "roles")
public class RoleEntity {
    @Id
    private String code;

    @Column(nullable = false)
    private String name;

    @Enumerated(EnumType.STRING)
    @Column(nullable = false)
    private Faction faction;

    // 능력 규칙이 아직 없는 직업은 null이다. 새 능력을 추가할 때 서비스 검증도 함께 확장한다.
    @Column(name = "action_code")
    private String actionCode;

    // 보류된 직업도 과거 방의 참조를 위해 남겨 두고 공개 목록에서만 제외한다.
    @Column(nullable = false)
    private boolean enabled;

    protected RoleEntity() {}

    public String getCode() { return code; }
    public String getName() { return name; }
    public Faction getFaction() { return faction; }
    public String getActionCode() { return actionCode; }
    public boolean isEnabled() { return enabled; }
}
