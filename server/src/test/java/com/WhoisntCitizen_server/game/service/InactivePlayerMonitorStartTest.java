// 서버가 하나일때만 적용 (연결 끊김 검사의 재시작 직후 유예)
package com.WhoisntCitizen_server.game.service;

import com.WhoisntCitizen_server.common.config.GamePhaseProperties;
import com.WhoisntCitizen_server.game.repository.GameRepository;
import com.WhoisntCitizen_server.support.MutableClock;
import org.junit.jupiter.api.Test;
import org.springframework.scheduling.TaskScheduler;

import java.time.Duration;
import java.time.Instant;

import static org.mockito.ArgumentMatchers.any;
import static org.mockito.ArgumentMatchers.eq;
import static org.mockito.Mockito.mock;
import static org.mockito.Mockito.verify;
import static org.mockito.Mockito.verifyNoInteractions;

/**
 * 서버 하나 기준:
 * 연결 끊김 검사가 서버가 켜진 즉시가 아니라 "기준 시간(inactiveTimeoutSeconds) 뒤"부터 시작되는지 확인한다.
 * 서버가 꺼져 있던 동안 멈춘 접속 기록 때문에, 다시 켜지자마자 게임이 취소되지 않게 하기 위해서다. (InactivePlayerMonitor.start)
 * 검사 자체(checkInactivePlayers)는 기존 테스트가 맡고, 여기서는 예약 시각만 본다.
 */
class InactivePlayerMonitorStartTest {

    private static final Instant NOW = Instant.parse("2026-10-19T12:00:00Z");

    private final TaskScheduler scheduler = mock(TaskScheduler.class);

    private InactivePlayerMonitor monitor(int inactiveTimeoutSeconds, int inactiveCheckSeconds) {
        // 밤 30, 결과 5, 낮 60, 투표 30, 처형 5, 종료 후 60초 보관, 연결 끊김 기준, 검사 주기, 10일 무사망 시 취소, 낮 연장 10
        GamePhaseProperties props = new GamePhaseProperties(30, 5, 60, 30, 5, 60,
                inactiveTimeoutSeconds, inactiveCheckSeconds, 10, 10);
        return new InactivePlayerMonitor(mock(GameRepository.class), mock(GameFlowService.class),
                scheduler, props, new MutableClock(NOW));
    }

    @Test
    void 첫_검사는_서버가_켜진_뒤_기준_시간만큼_지나서_시작한다() {
        monitor(60, 5).start();

        // 첫 검사 = 지금 + 60초, 그 뒤 5초마다
        verify(scheduler).scheduleWithFixedDelay(any(Runnable.class), eq(NOW.plusSeconds(60)), eq(Duration.ofSeconds(5)));
    }

    @Test
    void 검사가_꺼져_있으면_예약하지_않는다() {
        monitor(0, 5).start();

        verifyNoInteractions(scheduler);
    }
}
