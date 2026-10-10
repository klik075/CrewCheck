package com.WhoisntCitizen_server.game.lock;

import com.WhoisntCitizen_server.common.exception.LockTimeoutException;

import java.time.Duration;
import java.util.Objects;
import java.util.concurrent.ConcurrentHashMap;
import java.util.concurrent.TimeUnit;
import java.util.concurrent.locks.ReentrantLock;
import java.util.function.Supplier;

/**
 * 서버 메모리(JVM) 안의 게임 잠금. 서버 1대 기준이며, 여러 대로 늘리면 Redis 분산 락으로 바꾼다.
 * 예전의 synchronized(game)과 같은 동작이다. (게임 객체가 gameId당 하나라서 잠금 대상이 같다)
 * ReentrantLock을 쓰는 이유: synchronized처럼 재진입이 되고, tryLock으로 대기 시간을 제한할 수 있다.
 *
 * 대기 시간(waitTimeout) 안에 잠금을 잡지 못하면 LockTimeoutException을 던진다. (GameLock 계약)
 * 이미 이 스레드가 쥐고 있는 잠금(재진입)은 기다리지 않고 바로 잡는다.
 *
 * 끝난 게임의 잠금 객체는 지우지 않는다. 다른 스레드가 기다리는 중일 수 있어 안전하게 지우기 어렵고,
 * 게임 하나당 작은 객체 하나라 지금 규모에서는 문제가 되지 않는다.
 */
public class LocalGameLock implements GameLock {

    /** 대기 시간을 따로 주지 않을 때 (테스트 등) */
    public static final Duration DEFAULT_WAIT_TIMEOUT = Duration.ofSeconds(10);

    private final ConcurrentHashMap<String, ReentrantLock> locks = new ConcurrentHashMap<>();
    private final Duration waitTimeout;

    public LocalGameLock() {
        this(DEFAULT_WAIT_TIMEOUT);
    }

    public LocalGameLock(Duration waitTimeout) {
        this.waitTimeout = Objects.requireNonNull(waitTimeout, "waitTimeout");
    }

    @Override
    public <T> T withLock(String gameId, Supplier<T> action) {
        Objects.requireNonNull(gameId, "gameId");
        ReentrantLock lock = locks.computeIfAbsent(gameId, id -> new ReentrantLock());
        acquire(lock, gameId);
        try {
            return action.get();
        } finally {
            lock.unlock();
        }
    }

    private void acquire(ReentrantLock lock, String gameId) {
        try {
            if (!lock.tryLock(waitTimeout.toMillis(), TimeUnit.MILLISECONDS)) {
                throw new LockTimeoutException("게임", gameId, waitTimeout);
            }
        } catch (InterruptedException e) {
            Thread.currentThread().interrupt(); // 인터럽트 표시를 되살려 호출한 쪽이 알 수 있게 한다
            throw new IllegalStateException("게임 잠금을 기다리다 인터럽트되었습니다. (gameId=" + gameId + ")", e);
        }
    }
}
