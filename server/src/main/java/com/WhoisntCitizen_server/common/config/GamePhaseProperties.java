package com.WhoisntCitizen_server.common.config;

import org.springframework.boot.context.properties.ConfigurationProperties;
import org.springframework.boot.context.properties.bind.DefaultValue;

/** application.yml 의 mafia.phase.* 값 (초 단위). */
@ConfigurationProperties(prefix = "mafia.phase")
public record GamePhaseProperties(
        @DefaultValue("30") int nightSeconds,
        @DefaultValue("5") int nightResultSeconds,
        @DefaultValue("60") int daySeconds,
        @DefaultValue("30") int voteSeconds,
        @DefaultValue("5") int executionSeconds,
        // 게임 종료 후 결과 조회를 위해 메모리에 남겨 두는 시간. 지나면 InMemoryGameRepository에서 삭제
        @DefaultValue("60") int endedRetentionSeconds,
        // 마지막 요청 후 이 시간 동안 요청이 없으면 연결이 끊긴 것으로 보고 게임에서 내보낸다. 0 이하면 검사하지 않음
        @DefaultValue("60") int inactiveTimeoutSeconds,
        // 연결 끊김 검사 주기
        @DefaultValue("5") int inactiveCheckSeconds,
        // 이 일수 동안 연속으로 아무도 죽지 않으면 그날 투표 결과 직후 게임을 취소한다. 0 이하면 검사하지 않음
        @DefaultValue("10") int maxDaysWithoutDeath
) {
}
