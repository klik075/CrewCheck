package com.WhoisntCitizen_server.jobs.domain;

import com.WhoisntCitizen_server.game.entity.GamePhase;

/**
 * DB의 RoleEntity를 게임 로직에서 쓰기 위한 불변 값 객체.
 * 인메모리 Game/GamePlayer는 JPA 엔티티 대신 이 객체를 들고 다닌다.
 */
public record RoleDefinition(String code, String name, Faction faction, ActionCode actionCode) {

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
}
