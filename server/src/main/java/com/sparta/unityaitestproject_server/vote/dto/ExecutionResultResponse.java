package com.sparta.unityaitestproject_server.vote.dto;

import java.util.Map;

/** 7. 처형 결과 */
public record ExecutionResultResponse(
        int day,
        Long executedPlayerId,
        String executedNickname,
        boolean tie,
        Map<Long, Integer> voteCounts
) {
}
