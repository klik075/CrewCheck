package com.WhoisntCitizen_server.jobs.domain;

import com.WhoisntCitizen_server.game.entity.GamePhase;

/**
 * DB의 RoleEntity를 게임 로직에서 쓰기 위한 불변 값 객체.
 * 인메모리 Game/GamePlayer는 JPA 엔티티 대신 이 객체를 들고 다닌다.
 */
public record RoleDefinition(String code, String name, Faction faction, ActionCode actionCode) {

    // 접선 규칙처럼 특정 직업에만 적용되는 규칙에서 쓰는 roles.code 값
    public static final String RAIDER_CODE = "PIRATE_RAIDER";
    public static final String PARROT_CODE = "PIRATE_PARROT";
    public static final String MONKEY_CODE = "CREW_MONKEY";
    public static final String SIREN_CODE = "NEUTRAL_SIREN";
    public static final String KRAKEN_CODE = "NEUTRAL_KRAKEN";
    public static final String GHOST_CAPTAIN_CODE = "NEUTRAL_GHOST_CAPTAIN";
    public static final String MERMAID_CODE = "NEUTRAL_MERMAID";

    public static RoleDefinition from(RoleEntity e) {
        return new RoleDefinition(
                e.getCode(),
                e.getName(),
                e.getFaction(),
                e.getActionCode() == null ? null : ActionCode.valueOf(e.getActionCode()));
    }

    public boolean hasNightAction() {
        return actionCode != null && actionCode.phase() == GamePhase.NIGHT;
    }

    /** 해적 테마에서는 PIRATE 진영이 마피아팀이다. */
    public boolean isPirate() {
        return faction == Faction.PIRATE;
    }

    /** 공격할 수 있는 해적. 앵무새는 해적 진영이지만 여기에 속하지 않는다. */
    public boolean isRaider() {
        return RAIDER_CODE.equals(code);
    }

    public boolean isParrot() {
        return PARROT_CODE.equals(code);
    }

    /** 자신이 원숭이인 줄 모르고 위장 직업으로 행동하는 선원. 행동은 효과가 없고 가짜 결과를 받는다. */
    public boolean isMonkey() {
        return MONKEY_CODE.equals(code);
    }

    /** 선원·해적 어느 쪽도 아닌 제3 세력. 선장이 조사하면 선원(CREW)으로 보인다. */
    public boolean isNeutral() {
        return faction == Faction.NEUTRAL;
    }

    /** 유혹한 사람을 자기 팀(세이렌 팀)으로 만드는 제3 세력의 리더 */
    public boolean isSiren() {
        return SIREN_CODE.equals(code);
    }

    /** 표식을 남겼다가 한 번에 처치하는 제3 세력. 혼자 이긴다. */
    public boolean isKraken() {
        return KRAKEN_CODE.equals(code);
    }

    /** 밤에 죽지 않는 제3 세력. 사망자가 생존자보다 많아지면 혼자 이긴다. */
    public boolean isGhostCaptain() {
        return GHOST_CAPTAIN_CODE.equals(code);
    }

    /** 투표로 처형되면 혼자 이기는 제3 세력 */
    public boolean isMermaid() {
        return MERMAID_CODE.equals(code);
    }
}
