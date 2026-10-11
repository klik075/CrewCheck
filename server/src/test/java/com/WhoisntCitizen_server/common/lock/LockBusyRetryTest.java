package com.WhoisntCitizen_server.common.lock;

import com.WhoisntCitizen_server.common.exception.LockTimeoutException;
import com.WhoisntCitizen_server.support.ManualTaskScheduler;
import com.WhoisntCitizen_server.support.MutableClock;
import org.junit.jupiter.api.Test;

import java.time.Duration;
import java.time.Instant;
import java.util.concurrent.atomic.AtomicInteger;

import static org.assertj.core.api.Assertions.assertThat;
import static org.assertj.core.api.Assertions.assertThatCode;
import static org.assertj.core.api.Assertions.assertThatThrownBy;

/** 2-8: 잠금을 못 잡아 실패한 리스너 일을 1초 간격으로 최대 3번까지 시도하는지 확인한다. */
class LockBusyRetryTest {

    private static final Instant NOW = Instant.parse("2026-10-10T12:00:00Z");

    private final MutableClock clock = new MutableClock(NOW);
    private final ManualTaskScheduler scheduler = new ManualTaskScheduler(clock);
    private final LockBusyRetry retry = new LockBusyRetry(scheduler, clock);
    private final AtomicInteger calls = new AtomicInteger();

    /** failures번 잠금 대기 시간 초과로 실패한 뒤 성공하는 일 */
    private Runnable failing(int failures) {
        return () -> {
            if (calls.incrementAndGet() <= failures) {
                throw new LockTimeoutException("방", 1L, Duration.ofSeconds(10));
            }
        };
    }

    private void advanceOneSecond() {
        scheduler.advance(Duration.ofSeconds(1));
        scheduler.runDue();
    }

    @Test
    void 성공하면_한_번만_실행한다() {
        retry.run("테스트", failing(0));
        advanceOneSecond();

        assertThat(calls).hasValue(1);
    }

    @Test
    void 잠금을_못_잡으면_1초_뒤_다시_시도해_성공한다() {
        retry.run("테스트", failing(2));
        assertThat(calls).hasValue(1);

        advanceOneSecond();                  // 2번째 (실패)
        assertThat(calls).hasValue(2);
        advanceOneSecond();                  // 3번째 (성공)
        assertThat(calls).hasValue(3);

        advanceOneSecond();
        assertThat(calls).as("성공한 뒤에는 더 시도하지 않는다").hasValue(3);
    }

    @Test
    void 최대_횟수까지_실패하면_포기한다() {
        retry.run("테스트", failing(100));
        for (int i = 0; i < 5; i++) {
            advanceOneSecond();
        }

        assertThat(calls).hasValue(LockBusyRetry.MAX_ATTEMPTS);
    }

    @Test
    void 잠금이_아닌_실패는_다시_시도하지_않고_첫_시도에서는_그대로_던진다() {
        assertThatThrownBy(() -> retry.run("테스트", () -> {
            calls.incrementAndGet();
            throw new IllegalStateException("버그");
        })).isInstanceOf(IllegalStateException.class);
        advanceOneSecond();

        assertThat(calls).hasValue(1);
    }

    @Test
    void 다시_시도에서_다른_예외가_나도_스케줄러로_던지지_않는다() {
        retry.run("테스트", () -> {
            if (calls.incrementAndGet() == 1) {
                throw new LockTimeoutException("방", 1L, Duration.ofSeconds(10));
            }
            throw new IllegalStateException("두 번째에는 다른 이유로 실패");
        });

        assertThatCode(this::advanceOneSecond).doesNotThrowAnyException();
        assertThat(calls).hasValue(2);
    }
}
