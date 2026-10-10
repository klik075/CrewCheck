package com.WhoisntCitizen_server.game.scheduling.redis;

import org.slf4j.Logger;
import org.slf4j.LoggerFactory;
import org.springframework.context.SmartLifecycle;
import org.springframework.scheduling.TaskScheduler;

import java.time.Clock;
import java.time.Duration;
import java.time.Instant;
import java.util.Objects;
import java.util.concurrent.ScheduledFuture;
import java.util.function.IntSupplier;

/**
 * 서버가 켜져 있는 동안 짧은 간격(기본 250ms)으로 RedisGameTimer.pollOnce()를 부른다.
 * 서버마다 하나씩 돈다. 여러 서버가 같은 예약을 확인해도 한 서버만 가져간다. (RedisGameTimer의 Lua 가져가기)
 *
 * 동작
 *  - 서버가 켜지면(start) 바로 한 번 확인하고, 그 뒤로는 "확인이 끝난 시각 + 간격"마다 확인한다.
 *    확인이 끝난 뒤에 다음을 예약하므로 확인이 겹쳐 돌지 않는다. (fixed delay)
 *  - 한 번 확인에서 batchSize만큼 가득 가져왔으면 밀린 예약이 더 있다는 뜻이라 바로 다시 확인한다.
 *    한 번에 너무 오래 붙잡지 않도록 최대 MAX_ROUNDS_PER_TICK번까지만 이어서 확인한다.
 *  - 확인이 실패해도(Redis 연결 끊김 등) 멈추지 않고 다음 간격에 다시 확인한다.
 *    250ms마다 같은 경고가 쌓이지 않게 처음 실패와 FAILURE_LOG_EVERY번째마다만 남기고, 복구되면 알린다.
 *  - 서버가 꺼지면(stop) 다음 확인을 예약하지 않는다. 실행 중이던 확인은 끝까지 마친다.
 *    끝마치지 못하고 꺼져도 예약은 Redis에 남아 lease 뒤 다른 서버(또는 재시작한 이 서버)가 실행한다.
 *
 * 무엇을 확인할지는 IntSupplier(pollOnce)로 받는다. Redis 없이도 이 클래스를 테스트할 수 있게 하기 위해서다.
 * Spring이 SmartLifecycle로 켜고 끈다. 다른 Bean이 모두 준비된 뒤 시작하고, 꺼질 때는 먼저 멈춘다.
 */
public class RedisTimerPoller implements SmartLifecycle {

    /** 밀린 예약이 많을 때 한 번의 확인에서 이어서 부르는 최대 횟수 */
    static final int MAX_ROUNDS_PER_TICK = 10;
    /** 연속 실패를 이 횟수마다 한 번 로그로 남긴다 (250ms 간격이면 약 10초) */
    static final int FAILURE_LOG_EVERY = 40;

    private static final Logger log = LoggerFactory.getLogger(RedisTimerPoller.class);

    private final IntSupplier pollOnce;
    private final int batchSize;
    private final TaskScheduler scheduler;
    private final Clock clock;
    private final Duration interval;

    private volatile boolean running;
    /** start할 때마다 바뀐다. 멈췄다 다시 켰을 때 예전 예약이 남아 확인이 두 줄로 돌지 않게 한다 */
    private volatile long generation;
    private volatile ScheduledFuture<?> next;
    /** 확인은 겹쳐 돌지 않으므로(한 번에 하나) 따로 동기화하지 않는다 */
    private int consecutiveFailures;

    public RedisTimerPoller(IntSupplier pollOnce, int batchSize, TaskScheduler scheduler, Clock clock, Duration interval) {
        this.pollOnce = Objects.requireNonNull(pollOnce, "pollOnce");
        this.scheduler = Objects.requireNonNull(scheduler, "scheduler");
        this.clock = Objects.requireNonNull(clock, "clock");
        this.interval = Objects.requireNonNull(interval, "interval");
        if (batchSize <= 0) {
            throw new IllegalArgumentException("batchSize는 0보다 커야 합니다: " + batchSize);
        }
        if (interval.isNegative() || interval.isZero()) {
            throw new IllegalArgumentException("interval은 0보다 커야 합니다: " + interval);
        }
        this.batchSize = batchSize;
    }

    /** Redis 타이머로 만든다. */
    public static RedisTimerPoller of(RedisGameTimer timer, RedisTimerProperties props, TaskScheduler scheduler, Clock clock) {
        return new RedisTimerPoller(timer::pollOnce, props.batchSize(), scheduler, clock, props.pollInterval());
    }

    // ---------- 켜고 끄기 (SmartLifecycle) ----------

    @Override
    public synchronized void start() {
        if (running) {
            return;
        }
        running = true;
        generation++;
        log.info("Redis 게임 타이머 확인 시작: {}ms마다", interval.toMillis());
        scheduleNext(clock.instant(), generation); // 켜자마자 한 번 (꺼져 있는 동안 밀린 예약)
    }

    @Override
    public synchronized void stop() {
        if (!running) {
            return;
        }
        running = false;
        ScheduledFuture<?> pending = next;
        if (pending != null) {
            pending.cancel(false); // 실행 중인 확인은 끝까지 마친다
        }
        log.info("Redis 게임 타이머 확인 멈춤");
    }

    @Override
    public boolean isRunning() {
        return running;
    }

    // ---------- 확인 ----------

    private void scheduleNext(Instant at, long gen) {
        next = scheduler.schedule(() -> tick(gen), at);
    }

    /** 한 번의 확인. 끝나면 (켜져 있을 때만) 다음 확인을 예약한다. */
    void tick(long gen) {
        if (!running || gen != generation) {
            return; // 멈췄거나, 멈췄다 다시 켜기 전의 예약
        }
        try {
            pollUntilCaughtUp();
        } finally {
            if (running && gen == generation) {
                scheduleNext(clock.instant().plus(interval), gen);
            }
        }
    }

    /** batchSize만큼 가득 가져오는 동안 이어서 확인한다. 실행한 예약 수를 돌려준다. */
    int pollUntilCaughtUp() {
        int total = 0;
        try {
            for (int round = 0; round < MAX_ROUNDS_PER_TICK && running; round++) {
                int fired = pollOnce.getAsInt();
                total += fired;
                if (fired < batchSize) {
                    break;
                }
            }
            recovered();
        } catch (RuntimeException e) {
            failed(e);
        }
        return total;
    }

    private void recovered() {
        if (consecutiveFailures > 0) {
            log.info("Redis 게임 타이머 확인이 다시 됩니다 ({}번 연속 실패 후)", consecutiveFailures);
            consecutiveFailures = 0;
        }
    }

    private void failed(RuntimeException e) {
        consecutiveFailures++;
        if (consecutiveFailures == 1) {
            log.warn("Redis 게임 타이머 확인 실패. 다음 간격에 다시 확인합니다: {}", e.getMessage(), e);
        } else if (consecutiveFailures % FAILURE_LOG_EVERY == 0) {
            log.warn("Redis 게임 타이머 확인이 {}번 연속 실패 중입니다: {}", consecutiveFailures, e.getMessage());
        }
    }
}
