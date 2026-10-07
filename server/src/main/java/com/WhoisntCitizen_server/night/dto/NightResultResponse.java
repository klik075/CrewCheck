package com.WhoisntCitizen_server.night.dto;

import com.WhoisntCitizen_server.game.entity.DeathCause;
import com.WhoisntCitizen_server.jobs.domain.ActionCode;
import com.WhoisntCitizen_server.jobs.domain.Faction;
import com.WhoisntCitizen_server.night.entity.ReportType;

import java.util.List;

/**
 * 4. 밤 결과 공개.
 * killedPlayerId/killedNickname은 해적의 습격으로 죽은 사람(예전 클라이언트 호환).
 * deaths는 그날 밤 죽은 사람 전원과 원인(ATTACK = 해적, KRAKEN = 크라켄 → 컷신). 아무도 안 죽었으면 빈 목록.
 * reports는 요청자 본인이 그날 밤 받은 결과만 담는다. 없으면 빈 목록.
 */
public record NightResultResponse(
        int day,
        Long killedPlayerId,
        String killedNickname,
        boolean protectedByDoctor,
        List<ReportView> reports,
        List<DeathView> deaths
) {
    public record DeathView(Long playerId, String nickname, DeathCause cause) {
    }

    /**
     * type별로 채워지는 필드. 나머지는 null(목록은 빈 목록).
     * FACTION → faction, CORPSE_ROLE → roleCode/roleName, VISITORS → players, ACTIONS → actions
     */
    public record ReportView(ReportType type, Long targetId, String targetNickname,
                             Faction faction, String roleCode, String roleName,
                             List<PlayerRef> players, List<ActionView> actions) {
    }

    public record PlayerRef(Long playerId, String nickname) {
    }

    public record ActionView(ActionCode actionCode, Long targetId, String targetNickname) {
    }
}