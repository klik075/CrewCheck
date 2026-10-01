package com.WhoisntCitizen_server.game.dto;

import com.WhoisntCitizen_server.game.entity.GamePlayer;
import com.WhoisntCitizen_server.jobs.domain.ActionCode;
import com.WhoisntCitizen_server.jobs.domain.Faction;
import com.WhoisntCitizen_server.jobs.domain.RoleDefinition;

import java.util.List;

/**
 * 2. 내 역할 조회. 해적 진영에게는 알고 있는 동료 목록도 알려준다.
 * role = roles.code (예: PIRATE_RAIDER), actionCode가 null이면 능력 없음.
 * 직업 정보는 본인에게 보이는 직업(shownRole) 기준이다. 원숭이에게는 위장 직업을 보여 주고 실제 직업은 숨긴다.
 * remainingUses: 능력의 남은 사용 횟수. 무제한이거나 능력이 없으면 -1.
 * mafiaTeammateIds: 해적끼리는 처음부터 서로 보이고, 앵무새는 접선한 뒤에만 해적과 서로 보인다.
 * contacted: 앵무새가 해적과 접선했는지. 앵무새가 아니면 false.
 */
public record MyRoleResponse(
        Long playerId,
        String role,
        String roleName,
        Faction faction,
        ActionCode actionCode,
        int remainingUses,
        boolean alive,
        List<Long> mafiaTeammateIds,
        boolean contacted
) {
    public static MyRoleResponse of(GamePlayer me, List<Long> mafiaTeammateIds) {
        RoleDefinition shown = me.getShownRole();
        ActionCode code = shown.actionCode();
        int remainingUses = code == null ? -1 : me.remainingUses(code);
        return new MyRoleResponse(me.getPlayerId(), shown.code(), shown.name(),
                shown.faction(), code, remainingUses, me.isAlive(), mafiaTeammateIds, me.isContacted());
    }
}