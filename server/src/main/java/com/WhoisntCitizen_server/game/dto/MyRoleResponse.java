package com.WhoisntCitizen_server.game.dto;

import com.WhoisntCitizen_server.game.entity.GamePlayer;
import com.WhoisntCitizen_server.jobs.domain.ActionCode;
import com.WhoisntCitizen_server.jobs.domain.Faction;

import java.util.List;

/**
 * 2. 내 역할 조회. 마피아(해적)에게는 동료 목록도 알려준다.
 * role = roles.code (예: PIRATE_RAIDER), actionCode가 null이면 능력 없음.
 */
public record MyRoleResponse(
        Long playerId,
        String role,
        String roleName,
        Faction faction,
        ActionCode actionCode,
        boolean alive,
        List<Long> mafiaTeammateIds
) {
    public static MyRoleResponse of(GamePlayer me, List<Long> mafiaTeammateIds) {
        return new MyRoleResponse(me.getPlayerId(), me.getRole().code(), me.getRole().name(),
                me.getRole().faction(), me.getRole().actionCode(), me.isAlive(), mafiaTeammateIds);
    }
}
