package com.WhoisntCitizen_server.game.scheduling.redis;

import com.WhoisntCitizen_server.support.ManualTaskScheduler;
import com.WhoisntCitizen_server.support.MutableClock;
import org.junit.jupiter.api.BeforeEach;
import org.junit.jupiter.api.Test;

import java.time.Duration;
import java.time.Instant;
import java.util.ArrayDeque;
import java.util.Deque;
import java.util.function.IntSupplier;

import static org.assertj.core.api.Assertions.assertThat;
import static org.assertj.core.api.Assertions.assertThatCode;
import static org.assertj.core.api.Assertions.assertThatThrownBy;

/**
 * 3-4: RedisTimerPoller가 정해진 간격으로 확인하고, 밀린 예약을 이어서 처리하고, 실패해도 멈추지 않는지 확인한다.
 * Redis 없이 확인한다. 확인할 때마다 무엇을 돌려줄지(가져간 개수 또는 예외)를 FakePoll에 미리 넣어 둔다.
 */
class RedisTimerPollerTest {

    private static final Instant NOW = Instant.parse("2026-10-10T12:00:00Z");
    private static final Duration INTERVAL = Duration.ofMillis(250);
    private static final int BATCH = 100;

    /** 미리 넣어 둔 결과를 차례로 돌려주는 pollOnce. 넣어 둔 것이 없으면 0(가져간 것 없음) */
    private static final class FakePoll implements IntSupplier {
        private final Deque<Object> results = new ArrayDeque<>();
        private int calls;

        FakePoll then(Object... next) {
            for (Object r : next) {
                results.add(r);
            }
            return this;
        }

        @Override
        public int getAsInt() {
            calls++;
            Object r = results.poll();
            if (r instanceof RuntimeException e) {
                throw e;
            }
            return r == null ? 0 : (Integer) r;
        }
    }

    private MutableClock clock;
    private ManualTaskScheduler scheduler;
    private FakePoll poll;
    private RedisTimerPoller poller;

    @BeforeEach
    void setUp() {
        clock = new MutableClock(NOW);
        scheduler = new ManualTaskScheduler(clock);
        poll = new FakePoll();
        poller = new RedisTimerPoller(poll, BATCH, scheduler, clock, INTERVAL);
    }

    private void advance(Duration duration) {
        scheduler.advance(duration);
        scheduler.runDue();
    }

    @Test
    void 켜자마자_한_번_확인한다() {
        poller.start();
        scheduler.runDue();

        assertThat(poll.calls).isEqualTo(1);
        assertThat(poller.isRunning()).isTrue();
    }

    @Test
    void 켜기_전에는_확인하지_않는다() {
        advance(Duration.ofSeconds(10));

        assertThat(poll.calls).isZero();
    }

    @Test
    void 간격마다_확인한다() {
        poller.start();
        scheduler.runDue();                         // 1번

        advance(Duration.ofSeconds(1));             // 250ms 간격 → 4번 더

        assertThat(poll.calls).isEqualTo(5);
    }

    @Test
    void 가득_가져왔으면_밀린_예약이_있으니_바로_이어서_확인한다() {
        poll.then(BATCH, BATCH, 3);                 // 100, 100, 3개 → 세 번째에서 다 따라잡음
        poller.start();
        scheduler.runDue();

        assertThat(poll.calls).isEqualTo(3);
    }

    @Test
    void 이어서_확인하는_횟수에는_한도가_있다() {
        for (int i = 0; i < 50; i++) {
            poll.then(BATCH);                       // 계속 가득
        }
        poller.start();
        scheduler.runDue();

        assertThat(poll.calls).isEqualTo(RedisTimerPoller.MAX_ROUNDS_PER_TICK);

        advance(INTERVAL);                          // 나머지는 다음 간격에 이어서
        assertThat(poll.calls).isEqualTo(RedisTimerPoller.MAX_ROUNDS_PER_TICK * 2);
    }

    @Test
    void 확인이_실패해도_멈추지_않고_다음_간격에_다시_확인한다() {
        poll.then(new IllegalStateException("Redis 연결 끊김"), new IllegalStateException("Redis 연결 끊김"));
        poller.start();

        assertThatCode(() -> advance(INTERVAL.multipliedBy(4))).doesNotThrowAnyException();

        assertThat(poll.calls).as("실패한 2번 뒤에도 계속 확인").isEqualTo(5);
        assertThat(poller.isRunning()).isTrue();
    }

    @Test
    void 끄면_더_이상_확인하지_않는다() {
        poller.start();
        scheduler.runDue();
        poller.stop();

        advance(Duration.ofSeconds(10));

        assertThat(poll.calls).isEqualTo(1);
        assertThat(poller.isRunning()).isFalse();
    }

    @Test
    void 두_번_켜도_확인은_한_줄로만_돈다() {
        poller.start();
        poller.start();
        scheduler.runDue();

        advance(Duration.ofSeconds(1));

        assertThat(poll.calls).isEqualTo(5);
    }

    @Test
    void 껐다가_다시_켜도_예전_예약_때문에_두_줄로_돌지_않는다() {
        poller.start();
        scheduler.runDue();                         // 1번, 다음 확인이 250ms 뒤로 예약됨
        poller.stop();
        poller.start();                             // 바로 1번 더 예약됨 (예전 예약은 무시되어야 함)

        advance(Duration.ofSeconds(1));             // 다시 켠 뒤: 바로 1번 + 4번

        assertThat(poll.calls).isEqualTo(1 + 5);
    }

    @Test
    void 잘못된_설정은_거부한다() {
        assertThatThrownBy(() -> new RedisTimerPoller(poll, 0, scheduler, clock, INTERVAL))
                .isInstanceOf(IllegalArgumentException.class);
        assertThatThrownBy(() -> new RedisTimerPoller(poll, BATCH, scheduler, clock, Duration.ZERO))
                .isInstanceOf(IllegalArgumentException.class);
        assertThatThrownBy(() -> new RedisTimerProperties(0, 30, 100, 4)).isInstanceOf(IllegalArgumentException.class);
        assertThatThrownBy(() -> new RedisTimerProperties(250, 0, 100, 4)).isInstanceOf(IllegalArgumentException.class);
        assertThatThrownBy(() -> new RedisTimerProperties(250, 30, 0, 4)).isInstanceOf(IllegalArgumentException.class);
        assertThatThrownBy(() -> new RedisTimerProperties(250, 30, 100, 0)).isInstanceOf(IllegalArgumentException.class);
    }

    @Test
    void 설정의_기본값을_시간으로_바꾼다() {
        RedisTimerProperties props = new RedisTimerProperties(100, 30, 100, 4);

        assertThat(props.pollInterval()).isEqualTo(Duration.ofMillis(100));
        assertThat(props.lease()).isEqualTo(Duration.ofSeconds(30));
    }
}
