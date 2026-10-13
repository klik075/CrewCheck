package com.WhoisntCitizen_server.vote.dto;

import java.util.List;
import java.util.Map;

/**
 * 7. 처형 결과.
 * votes = 득표 목록(득표 많은 순, 같으면 playerId 순). Unity JsonUtility는 Map을 읽지 못해서 목록으로도 준다.
 * voteCounts(playerId → 득표 수)는 기존 클라이언트·Postman 호환용으로 남긴다.
 */
public record ExecutionResultResponse(
        int day,
        Long executedPlayerId,
        String executedNickname,
        boolean tie,
        Map<Long, Integer> voteCounts,
        List<VoteCount> votes
) {
    public record VoteCount(Long playerId, String nickname, int count) {
    }
}