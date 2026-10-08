package com.WhoisntCitizen_server.game.scheduling;

import org.slf4j.Logger;
import org.slf4j.LoggerFactory;
import org.springframework.context.ApplicationEventPublisher;
import org.springframework.scheduling.TaskScheduler;

import java.time.Clock;

/**
 * 스케줄러에 "지금 바로" 예약해서 다른 스레드(타이머 스레드)에서 이벤트를 발행한다.
 * 호출한 스레드는 게임 잠금을 쥔 채로 바로 돌아가고, 리스너는 잠금이 풀린 뒤 실행된다.
 * 리스너에서 예외가 나도 게임 진행 스레드에는 영향이 없도록 로그만 남긴다.
 */
public class SchedulerDeferredEventPublisher implements DeferredEventPublisher {

    private static final Logger log = LoggerFactory.getLogger(SchedulerDeferredEventPublisher.class);

    private final TaskScheduler scheduler;
    private final Clock clock;
    private final ApplicationEventPublisher eventPublisher;

    public SchedulerDeferredEventPublisher(TaskScheduler scheduler, Clock clock, ApplicationEventPublisher eventPublisher) {
        this.scheduler = scheduler;
        this.clock = clock;
        this.eventPublisher = eventPublisher;
    }

    @Override
    public void publishAfterLock(Object event) {
        scheduler.schedule(() -> {
            try {
                eventPublisher.publishEvent(event);
            } catch (RuntimeException e) {
                log.error("이벤트 처리 실패: {}", event, e);
            }
        }, clock.instant());
    }
}
