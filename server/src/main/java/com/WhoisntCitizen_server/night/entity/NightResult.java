package com.WhoisntCitizen_server.night.entity;

import java.util.List;
import java.util.Map;

/**
 * 밤 결과. killedPlayerId/protectedByDoctor는 전체 공개,
 * reports(받는 사람 id -> 결과 목록)는 받는 사람 본인에게만 공개한다.
 */
public record NightResult(
        int day,
        Long killedPlayerId,
        boolean protectedByDoctor,
        Map<Long, List<PrivateReport>> reports
) {
    public List<PrivateReport> reportsFor(Long playerId) {
        return reports.getOrDefault(playerId, List.of());
    }
}