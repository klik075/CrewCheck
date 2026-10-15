package com.WhoisntCitizen_server.game.dto;

import com.WhoisntCitizen_server.game.entity.GamePhase;

import java.time.Instant;

/**
 * 낮 토론 시간 연장 접수 결과.
 * 연장은 페이즈 전환이 아니므로 phaseVersion은 그대로이고, phaseEndsAt만 늦춰진다. (이후 상태 조회에도 같은 값이 보인다)
 * addedSeconds = 이번 요청으로 늘어난 초. 이번 낮에 이미 늘린 사람이 다시 보내면 시간은 그대로 두고 0.
 * extendedCount = 이번 낮에 토론 시간을 늘린 생존자 수.
 */
public record DayExtendResponse(boolean accepted, GamePhase phase, long phaseVersion, Instant phaseEndsAt,
                                long addedSeconds, long extendedCount) {
}
