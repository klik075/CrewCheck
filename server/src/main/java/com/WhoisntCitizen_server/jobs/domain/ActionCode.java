package com.WhoisntCitizen_server.jobs.domain;

import com.WhoisntCitizen_server.game.entity.GamePhase;

/**
 * 행동 정책의 한곳짜리 정의. enum 이름은 API actionCode 및 roles.action_code와 일치해야 한다.
 * 새 능력을 만들 때는 이곳, 새 Flyway 마이그레이션, Game.recordNightAction 검증,
 * NightActionResolver 판정(밤 능력인 경우)을 함께 수정한다.
 * maxUses는 게임 전체 제한이며 -1은 무제한이다. 같은 밤의 재제출은 판정 전까지 덮어쓰고, 횟수는 판정 시점에 차감한다.
 */
public enum ActionCode {
    INVESTIGATE_FACTION(GamePhase.NIGHT, -1, true, false),
    PROTECT(GamePhase.NIGHT, -1, true, true),
    WATCH_VISITORS(GamePhase.NIGHT, -1, true, true),
    BLOCK(GamePhase.NIGHT, -1, true, false),
//    DAY_SHOOT(GamePhase.DAY, 1, true, false),
    READ_CORPSE_ROLE(GamePhase.NIGHT, 2, false, false),
    SELECT_ATTACK_TARGET(GamePhase.NIGHT, -1, true, false),
    WATCH_ACTION(GamePhase.NIGHT, -1, true, false);

    // 생성자 순서: 사용 단계, 게임 전체 최대 횟수, 살아 있는 대상 필요 여부, 자기 자신 선택 허용 여부.
    private final GamePhase phase;
    private final int maxUses;
    private final boolean livingTarget;
    private final boolean selfTargetAllowed;

    ActionCode(GamePhase phase, int maxUses, boolean livingTarget, boolean selfTargetAllowed) {
        this.phase = phase;
        this.maxUses = maxUses;
        this.livingTarget = livingTarget;
        this.selfTargetAllowed = selfTargetAllowed;
    }

    public GamePhase phase() { return phase; }
    public int maxUses() { return maxUses; }
    public boolean requiresLivingTarget() { return livingTarget; }
    public boolean allowsSelfTarget() { return selfTargetAllowed; }
}
