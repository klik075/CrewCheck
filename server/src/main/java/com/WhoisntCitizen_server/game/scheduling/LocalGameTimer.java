package com.WhoisntCitizen_server.game.scheduling;

import org.springframework.scheduling.TaskScheduler;

import java.time.Instant;
import java.util.function.Supplier;

/**
 * 서버 메모리의 TaskScheduler로 예약하는 GameTimer. (서버 1대 기준, 서버가 꺼지면 예약도 사라진다)
 * handler는 Supplier로 받는다. GameFlowService가 GameTimer를 주입받고 동시에 handler이기도 해서,
 * 생성 시점에 바로 받으면 순환 의존이 생기기 때문이다. 실제 호출 시점에 꺼내 쓴다.
 */
public class LocalGameTimer implements GameTimer {

    private final TaskScheduler scheduler;
    private final Supplier<GameTimeoutHandler> handler;

    public LocalGameTimer(TaskScheduler scheduler, Supplier<GameTimeoutHandler> handler) {
        this.scheduler = scheduler;
        this.handler = handler;
    }

    @Override
    public void schedulePhaseTimeout(String gameId, long phaseVersion, Instant at) {
        scheduler.schedule(() -> handler.get().onPhaseTimeout(gameId, phaseVersion), at);
    }

    @Override
    public void scheduleCleanup(String gameId, Instant at) {
        scheduler.schedule(() -> handler.get().onCleanup(gameId), at);
    }
}
