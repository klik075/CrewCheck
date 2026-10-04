package com.WhoisntCitizen_server.jobs.dto;

import com.WhoisntCitizen_server.jobs.domain.Faction;
import com.WhoisntCitizen_server.jobs.domain.RoleDefinition;
import com.WhoisntCitizen_server.jobs.domain.RoleEntity;
import java.util.List;

/** 직업 목록 API의 JSON 계약. 필드 이름을 바꾸면 Unity 쪽 클래스도 함께 수정한다. */
public final class RoleApiDtos {
    private RoleApiDtos() {}

    public record RoleView(String code, String name, Faction faction, String actionCode) {
        public static RoleView from(RoleEntity role) {
            return new RoleView(role.getCode(), role.getName(), role.getFaction(), role.getActionCode());
        }

        public static RoleView from(RoleDefinition role) {
            return new RoleView(role.code(), role.name(), role.faction(),
                    role.actionCode() == null ? null : role.actionCode().name());
        }
    }

    public record RoleListResponse(List<RoleView> roles) {}
}
