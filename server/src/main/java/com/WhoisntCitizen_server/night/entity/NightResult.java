package com.WhoisntCitizen_server.night.entity;

import com.WhoisntCitizen_server.game.entity.DeathCause;

import java.util.List;
import java.util.Map;

/**
 * 밤 결과. killedPlayerId/protectedByDoctor/deaths는 전체 공개,
 * reports(받는 사람 id -> 결과 목록)는 받는 사람 본인에게만 공개한다.
 * killedPlayerId는 해적의 습격으로 죽은 사람(예전 클라이언트 호환), deaths는 그날 밤 죽은 사람 전원(크라켄 포함)이다.
 */
public record NightResult(
        int day,
        Long killedPlayerId,
        boolean protectedByDoctor,
        List<Death> deaths,
        Map<Long, List<PrivateReport>> reports
) {
    /** 밤에 죽은 사람 한 명과 원인(ATTACK = 해적, KRAKEN = 크라켄). 클라이언트는 원인별로 사망 연출을 고른다. */
    public record Death(Long playerId, DeathCause cause) {
    }

    public List<PrivateReport> reportsFor(Long playerId) {
        return reports.getOrDefault(playerId, List.of());
    }
}
