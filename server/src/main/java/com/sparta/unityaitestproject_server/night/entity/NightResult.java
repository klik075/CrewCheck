package com.sparta.unityaitestproject_server.night.entity;

import java.util.Map;

/**
 * 밤 결과. killedPlayerId/protectedByDoctor는 전체 공개,
 * investigations(경찰 id -> 조사 결과)는 해당 경찰에게만 공개한다.
 */
public record NightResult(
        int day,
        Long killedPlayerId,
        boolean protectedByDoctor,
        Map<Long, Investigation> investigations
) {
    public record Investigation(Long targetId, boolean mafia) {
    }
}
