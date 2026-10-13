package com.WhoisntCitizen_server.game.scheduling;

import com.WhoisntCitizen_server.game.lock.GameLockScope;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;
import org.springframework.context.ApplicationEventPublisher;
import org.springframework.scheduling.TaskScheduler;

import java.time.Clock;

/**
 * 게임 잠금이 풀린 뒤 스케줄러에 "지금 바로" 예약해서 다른 스레드(타이머 스레드)에서 이벤트를 발행한다.
 *  - 예약 자체를 GameLockScope.afterUnlock으로 미룬다. 예전에는 잠금 안에서 바로 예약해서, 스케줄러 스레드가
 *    빨리 돌면 호출한 쪽이 아직 잠금을 쥔 채로 리스너가 시작될 수 있었다. 이제는 "잠금이 풀린 뒤"가 보장된다.
 *  - 리스너(로비 방 복귀, 전적 저장)는 다른 스레드에서 돌므로 게임을 진행한 스레드(API 요청, 타이머)는 기다리지 않는다.
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
        GameLockScope.afterUnlock(() -> scheduler.schedule(() -> {
            try {
                eventPublisher.publishEvent(event);
            } catch (RuntimeException e) {
                log.error("이벤트 처리 실패: {}", event, e);
            }
        }, clock.instant()));
    }
}
