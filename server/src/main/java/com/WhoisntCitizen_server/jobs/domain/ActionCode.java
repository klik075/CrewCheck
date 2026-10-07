package com.WhoisntCitizen_server.jobs.domain;

import com.WhoisntCitizen_server.game.entity.GamePhase;

import java.util.List;

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
    WATCH_ACTION(GamePhase.NIGHT, -1, true, false),
    BAN_VOTE(GamePhase.NIGHT, -1, true, false), // 요리사: 대상은 다음 투표를 할 수 없다
    SEDUCE(GamePhase.NIGHT, -1, true, false), // 세이렌: 선원 진영·인어를 세이렌 팀으로. 성공한 다음 밤은 쉰다
    KRAKEN_MARK(GamePhase.NIGHT, -1, true, false), // 크라켄: 표식을 남긴다(방문)
    KRAKEN_STRIKE(GamePhase.NIGHT, -1, false, false, false); // 크라켄: 표식된 사람을 모두 처치하고 표식을 지운다(대상 없음, 방문 아님)

    // 생성자 순서: 사용 단계, 게임 전체 최대 횟수, 살아 있는 대상 필요 여부, 자기 자신 선택 허용 여부, 대상 필요 여부.
    private final GamePhase phase;
    private final int maxUses;
    private final boolean livingTarget;
    private final boolean selfTargetAllowed;
    private final boolean needsTarget;

    ActionCode(GamePhase phase, int maxUses, boolean livingTarget, boolean selfTargetAllowed) {
        this(phase, maxUses, livingTarget, selfTargetAllowed, true);
    }

    ActionCode(GamePhase phase, int maxUses, boolean livingTarget, boolean selfTargetAllowed, boolean needsTarget) {
        this.phase = phase;
        this.maxUses = maxUses;
        this.livingTarget = livingTarget;
        this.selfTargetAllowed = selfTargetAllowed;
        this.needsTarget = needsTarget;
    }

    public GamePhase phase() { return phase; }
    public int maxUses() { return maxUses; }
    public boolean requiresLivingTarget() { return livingTarget; }
    public boolean allowsSelfTarget() { return selfTargetAllowed; }
    public boolean needsTarget() { return needsTarget; }

    /**
     * 이 능력(roles.action_code)을 가진 직업이 밤에 고를 수 있는 능력. 대부분 자기 자신 하나이고,
     * 크라켄만 표식 대신 발동을 고를 수 있다. 요청에서 actionCode를 생략하면 자기 자신(기본 능력)이다.
     */
    public List<ActionCode> choices() {
        return this == KRAKEN_MARK ? List.of(KRAKEN_MARK, KRAKEN_STRIKE) : List.of(this);
    }
}
