package com.WhoisntCitizen_server.game.repository.snapshot;

import com.WhoisntCitizen_server.jobs.domain.ActionCode;
import com.WhoisntCitizen_server.jobs.domain.Faction;
import com.WhoisntCitizen_server.jobs.domain.RoleDefinition;

/**
 * 저장용 직업 정보. RoleDefinition과 값은 같지만, RoleDefinition에는 isPirate() 같은 판단 메서드가 있어
 * JSON 변환기가 이를 속성으로 읽어 저장 형식에 섞일 수 있으므로 저장 전용 record를 따로 둔다.
 * 직업 표(DB)가 게임 도중 바뀌어도 이미 시작한 게임은 시작할 때의 직업 정의로 끝까지 진행되도록 통째로 저장한다.
 */
public record RoleSnapshot(String code, String name, Faction faction, ActionCode actionCode) {

    public static RoleSnapshot from(RoleDefinition role) {
        return role == null ? null : new RoleSnapshot(role.code(), role.name(), role.faction(), role.actionCode());
    }

    public RoleDefinition toRole() {
        return new RoleDefinition(code, name, faction, actionCode);
    }
}
