package com.WhoisntCitizen_server.game.scheduling.redis;

import org.slf4j.Logger;
import org.slf4j.LoggerFactory;

import java.time.Duration;
import java.util.List;
import java.util.Objects;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;
import java.util.concurrent.Semaphore;
import java.util.concurrent.ThreadFactory;
import java.util.concurrent.TimeUnit;
import java.util.concurrent.atomic.AtomicInteger;

/**
 * 시간이 된 Redis 타이머 예약을 가져가서 작업 스레드에 하나씩 넘긴다. RedisTimerPoller가 주기적으로 dispatchOnce를 부른다.
 *
 * 왜 나누나
 *  pollOnce처럼 확인 스레드에서 차례로 실행하면, 한 게임의 타이머가 게임 잠금을 기다리는 동안(최대 10초)
 *  같은 서버의 다른 게임 타이머가 모두 밀린다. 그래서 확인(가져가기)은 한 스레드가 하고,
 *  실행은 작업 스레드 여러 개(workers, 기본 4)가 예약마다 따로 한다.
 *
 * 빈 작업 스레드 수만큼만 가져간다
 *  가져간 예약은 lease(30초) 안에 실행해야 한다. 작업 스레드가 모두 바쁜데 더 가져와 대기열에 쌓으면, 기다리는 동안
 *  lease가 지나 다른 서버가 다시 실행할 수 있다. 그래서 지금 바로 실행할 수 있는 개수(빈 스레드 수)만 가져간다.
 *  가져가지 않은 예약은 Redis에 그대로 있어 다음 확인 때나 다른 서버가 가져간다.
 *
 * 처리량: 실행 하나가 수 ms라 작업 스레드는 금방 비고, RedisTimerPoller가 한 번 확인할 때 최대 10번까지 이어서 가져간다.
 * 서버가 여러 대면 서버마다 따로 가져가므로 처리량도 늘어난다.
 *
 * 서버가 꺼질 때 close()로 작업 스레드를 멈춘다. 실행 중인 것은 잠깐 기다려 주고, 못 끝낸 예약은 lease 뒤 다른 서버가 실행한다.
 */
public class RedisTimerDispatcher implements AutoCloseable {

    /** 서버가 꺼질 때 실행 중인 타이머를 기다려 주는 최대 시간 */
    static final Duration SHUTDOWN_WAIT = Duration.ofSeconds(5);

    private static final Logger log = LoggerFactory.getLogger(RedisTimerDispatcher.class);

    private final RedisGameTimer timer;
    private final int workers;
    private final ExecutorService executor;
    /** 빈 작업 스레드 수. 가져가기 전에 빈 만큼 잡고, 실행이 끝나면 돌려준다 */
    private final Semaphore freeWorkers;

    public RedisTimerDispatcher(RedisGameTimer timer, int workers) {
        this.timer = Objects.requireNonNull(timer, "timer");
        if (workers <= 0) {
            throw new IllegalArgumentException("workers는 0보다 커야 합니다: " + workers);
        }
        this.workers = workers;
        this.freeWorkers = new Semaphore(workers);
        this.executor = Executors.newFixedThreadPool(workers, namedThreads());
    }

    public int workers() {
        return workers;
    }

    /**
     * 빈 작업 스레드 수만큼 시간이 된 예약을 가져가 작업 스레드에 넘긴다. 넘긴 개수를 돌려준다. (실행이 끝나기를 기다리지 않는다)
     * 한 스레드(RedisTimerPoller)에서만 부른다.
     */
    public int dispatchOnce() {
        int free = freeWorkers.availablePermits();
        if (free <= 0 || !freeWorkers.tryAcquire(free)) {
            return 0; // 모두 바쁨. 다음 확인 때 다시
        }
        List<RedisGameTimer.Claimed> claimed;
        try {
            claimed = timer.claimDue(free);
        } catch (RuntimeException e) {
            freeWorkers.release(free);
            throw e; // Redis 연결 실패 등. RedisTimerPoller가 로그를 남기고 다음 간격에 다시 확인한다
        }
        freeWorkers.release(free - claimed.size()); // 가져온 것보다 많이 잡아 둔 만큼 돌려준다
        for (RedisGameTimer.Claimed c : claimed) {
            submit(c);
        }
        return claimed.size();
    }

    private void submit(RedisGameTimer.Claimed c) {
        try {
            executor.execute(() -> {
                try {
                    timer.runClaimed(c);
                } finally {
                    freeWorkers.release();
                }
            });
        } catch (RuntimeException e) {
            // 서버가 꺼지는 중이라 작업 스레드가 받지 않음. 예약은 Redis에 남아 lease 뒤 다른 서버가 실행한다
            freeWorkers.release();
            log.warn("작업 스레드가 타이머를 받지 않아 다른 서버에 맡깁니다: {} ({})", c.key(), e.getMessage());
        }
    }

    /** 지금 실행 중인 타이머 수 (테스트·점검용) */
    int running() {
        return workers - freeWorkers.availablePermits();
    }

    /** 작업 스레드를 멈춘다. 실행 중인 타이머는 SHUTDOWN_WAIT까지 기다린다. */
    @Override
    public void close() {
        executor.shutdown();
        try {
            if (!executor.awaitTermination(SHUTDOWN_WAIT.toMillis(), TimeUnit.MILLISECONDS)) {
                log.warn("실행 중인 게임 타이머가 {}초 안에 끝나지 않아 멈춥니다. 남은 예약은 lease 뒤 다른 서버가 실행합니다",
                        SHUTDOWN_WAIT.toSeconds());
                executor.shutdownNow();
            }
        } catch (InterruptedException e) {
            executor.shutdownNow();
            Thread.currentThread().interrupt();
        }
    }

    private static ThreadFactory namedThreads() {
        AtomicInteger seq = new AtomicInteger();
        return runnable -> {
            Thread thread = new Thread(runnable, "game-timer-" + seq.incrementAndGet());
            thread.setDaemon(true);
            return thread;
        };
    }
}
