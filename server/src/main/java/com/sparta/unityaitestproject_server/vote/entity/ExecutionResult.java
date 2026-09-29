package com.sparta.unityaitestproject_server.vote.entity;

import java.util.Map;

/** 투표 집계 및 처형 결과. executedPlayerId가 null이면 동률/무투표로 처형 없음. */
public record ExecutionResult(
        int day,
        Long executedPlayerId,
        boolean tie,
        Map<Long, Integer> voteCounts
) {
}
