package com.WhoisntCitizen_server.night.entity;

import com.WhoisntCitizen_server.jobs.domain.ActionCode;
import com.WhoisntCitizen_server.jobs.domain.Faction;
import com.WhoisntCitizen_server.jobs.domain.RoleDefinition;

import java.util.List;

/**
 * 본인에게만 공개되는 밤 결과 한 건. type에 따라 채워지는 필드가 다르다.
 * Unity JsonUtility가 다형성을 지원하지 않으므로 하위 타입 대신 한 레코드에 nullable 필드를 둔다.
 * 목록 필드는 null 대신 빈 목록을 쓴다. 생성은 정적 팩토리로만 한다.
 */
public record PrivateReport(
        ReportType type,
        Long targetId,
        Faction faction,               // FACTION
        String roleCode,               // CORPSE_ROLE
        String roleName,               // CORPSE_ROLE
        List<Long> playerIds,          // VISITORS: 방문자 id (오름차순)
        List<ObservedAction> actions   // ACTIONS: 대상이 한 행동
) {
    /** 앵무새가 본 행동 하나. */
    public record ObservedAction(ActionCode code, Long targetId) {
    }

    public static PrivateReport faction(Long targetId, Faction faction) {
        return new PrivateReport(ReportType.FACTION, targetId, faction, null, null, List.of(), List.of());
    }

    /** 직업을 RoleDefinition으로 받는다. 원숭이의 가짜 결과(5단계)도 같은 팩토리로 만든다. */
    public static PrivateReport corpseRole(Long targetId, RoleDefinition role) {
        return new PrivateReport(ReportType.CORPSE_ROLE, targetId, null, role.code(), role.name(), List.of(), List.of());
    }

    public static PrivateReport visitors(Long targetId, List<Long> visitorIds) {
        return new PrivateReport(ReportType.VISITORS, targetId, null, null, null, List.copyOf(visitorIds), List.of());
    }

    public static PrivateReport actions(Long targetId, List<ObservedAction> actions) {
        return new PrivateReport(ReportType.ACTIONS, targetId, null, null, null, List.of(), List.copyOf(actions));
    }

    /** 갑판장: 대상을 차단했다. */
    public static PrivateReport block(Long targetId) {
        return new PrivateReport(ReportType.BLOCK, targetId, null, null, null, List.of(), List.of());
    }

    /** 갑판장에게 차단당해 이번 밤 능력을 쓰지 못했다. 누가 막았는지는 알려 주지 않는다. */
    public static PrivateReport blocked() {
        return new PrivateReport(ReportType.BLOCKED, null, null, null, null, List.of(), List.of());
    }
}