package com.WhoisntCitizen_server.vote.dto;

/**
 * 투표 요청.
 * targetId: 투표 대상. null 또는 0이면 표를 거둔다(기권).
 * confirm : true면 "투표 완료"(지금 상태로 고정). 생략(null)하면 기존 클라이언트 호환을 위해 true로 본다.
 */
public record VoteRequest(Long targetId, Boolean confirm) {

    public boolean confirmed() {
        return confirm == null || confirm;
    }
}
