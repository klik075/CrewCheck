package com.WhoisntCitizen_server.game.dto;

import com.WhoisntCitizen_server.game.entity.GamePhase;

/**
 * 낮 토론 넘기기 접수 결과.
 * 살아 있는 전원이 넘겨 바로 투표로 넘어갔으면 phase가 VOTE로 바뀌어 있다.
 * skippedCount / requiredCount = 넘긴 생존자 수 / 살아 있는 플레이어 수.
 */
public record DaySkipResponse(boolean accepted, GamePhase phase, long phaseVersion, long skippedCount, long requiredCount) {
}
