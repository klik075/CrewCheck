package com.WhoisntCitizen_server.jobs.domain;

/**
 * 행동 정책의 한곳짜리 정의. enum 이름은 API actionCode 및 roles.action_code와 일치해야 한다.
 * 새 능력을 만들 때는 이곳, 새 Flyway 마이그레이션, RoleService 검증,
 * NightResolutionService 판정(밤 능력인 경우)을 함께 수정한다.
 * maxUses는 게임 전체 제한이며 -1은 무제한이다. 같은 라운드의 중복 제출은 별도 DB 제약으로 막는다.
 */
public enum ActionCode {
    INVESTIGATE_FACTION(RoomPhase.NIGHT, -1, true, false),
    PROTECT(RoomPhase.NIGHT, -1, true, true),
    WATCH_VISITORS(RoomPhase.NIGHT, -1, true, true),
    BLOCK(RoomPhase.NIGHT, -1, true, false),
    DAY_SHOOT(RoomPhase.DAY, 1, true, false),
    READ_CORPSE_ROLE(RoomPhase.NIGHT, 2, false, false),
    SELECT_ATTACK_TARGET(RoomPhase.NIGHT, -1, true, false),
    // V2에서 제출된 행동을 읽을 수 있도록 남긴 이전 코드. 새 직업에는 배정하지 않는다.
    TEAM_ATTACK_VOTE(RoomPhase.NIGHT, -1, true, false),
    WATCH_ACTION(RoomPhase.NIGHT, -1, true, false);

    // 생성자 순서: 사용 단계, 게임 전체 최대 횟수, 살아 있는 대상 필요 여부, 자기 자신 선택 허용 여부.
    private final RoomPhase phase;
    private final int maxUses;
    private final boolean livingTarget;
    private final boolean selfTargetAllowed;

    ActionCode(RoomPhase phase, int maxUses, boolean livingTarget, boolean selfTargetAllowed) {
        this.phase = phase;
        this.maxUses = maxUses;
        this.livingTarget = livingTarget;
        this.selfTargetAllowed = selfTargetAllowed;
    }

    public RoomPhase phase() { return phase; }
    public int maxUses() { return maxUses; }
    public boolean requiresLivingTarget() { return livingTarget; }
    public boolean allowsSelfTarget() { return selfTargetAllowed; }
}
