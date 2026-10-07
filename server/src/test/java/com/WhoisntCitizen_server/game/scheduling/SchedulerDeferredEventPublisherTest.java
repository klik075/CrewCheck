package com.WhoisntCitizen_server.game.scheduling;

import com.WhoisntCitizen_server.support.ManualTaskScheduler;
import com.WhoisntCitizen_server.support.MutableClock;
import org.junit.jupiter.api.Test;

import java.time.Instant;
import java.util.ArrayList;
import java.util.List;

import static org.assertj.core.api.Assertions.assertThat;
import static org.assertj.core.api.Assertions.assertThatCode;

class SchedulerDeferredEventPublisherTest {

    private static final Instant NOW = Instant.parse("2026-10-07T12:00:00Z");

    private final MutableClock clock = new MutableClock(NOW);
    private final ManualTaskScheduler scheduler = new ManualTaskScheduler(clock);

    @Test
    void 호출한_자리에서_바로_발행하지_않고_스케줄러가_실행할_때_발행한다() {
        List<Object> events = new ArrayList<>();
        DeferredEventPublisher publisher = new SchedulerDeferredEventPublisher(scheduler, clock, events::add);

        publisher.publishAfterLock("game-ended");
        assertThat(events).isEmpty();          // 게임 잠금을 쥔 호출자 쪽에서는 아직 처리되지 않음

        scheduler.runDue();
        assertThat(events).containsExactly("game-ended");
    }

    @Test
    void 리스너_예외는_밖으로_던지지_않는다() {
        DeferredEventPublisher publisher = new SchedulerDeferredEventPublisher(scheduler, clock, event -> {
            throw new IllegalStateException("listener failed");
        });

        publisher.publishAfterLock("game-ended");

        assertThatCode(scheduler::runDue).doesNotThrowAnyException();
    }
}
