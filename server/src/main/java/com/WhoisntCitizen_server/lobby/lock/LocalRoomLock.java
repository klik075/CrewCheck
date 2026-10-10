package com.WhoisntCitizen_server.lobby.lock;

import com.WhoisntCitizen_server.common.exception.LockTimeoutException;

import java.time.Duration;
import java.util.Objects;
import java.util.concurrent.ConcurrentHashMap;
import java.util.concurrent.TimeUnit;
import java.util.concurrent.locks.ReentrantLock;
import java.util.function.Supplier;

/**
 * 서버 메모리(JVM) 안의 방 잠금. 서버 1대 기준이며, 여러 대로 늘리면 Redis 분산 락으로 바꾼다.
 * 예전 RoomLockManager(roomId별 synchronized)와 같은 동작이다.
 * ReentrantLock을 쓰는 이유: synchronized처럼 재진입이 되고, tryLock으로 대기 시간을 제한할 수 있다.
 * (게임 잠금 LocalGameLock과 같은 방식)
 *
 * 대기 시간(waitTimeout) 안에 잠금을 잡지 못하면 LockTimeoutException을 던진다. (RoomLock 계약)
 *
 * 삭제된 방의 잠금 객체는 지우지 않는다. 다른 스레드가 기다리는 중일 수 있어 안전하게 지우기 어렵고,
 * 방 하나당 작은 객체 하나라 지금 규모에서는 문제가 되지 않는다.
 */
public class LocalRoomLock implements RoomLock {

    /** 대기 시간을 따로 주지 않을 때 (테스트 등) */
    public static final Duration DEFAULT_WAIT_TIMEOUT = Duration.ofSeconds(10);

    private final ConcurrentHashMap<Long, ReentrantLock> locks = new ConcurrentHashMap<>();
    private final Duration waitTimeout;

    public LocalRoomLock() {
        this(DEFAULT_WAIT_TIMEOUT);
    }

    public LocalRoomLock(Duration waitTimeout) {
        this.waitTimeout = Objects.requireNonNull(waitTimeout, "waitTimeout");
    }

    @Override
    public <T> T withLock(Long roomId, Supplier<T> action) {
        Objects.requireNonNull(roomId, "roomId");
        ReentrantLock lock = locks.computeIfAbsent(roomId, id -> new ReentrantLock());
        acquire(lock, roomId);
        try {
            return action.get();
        } finally {
            lock.unlock();
        }
    }

    private void acquire(ReentrantLock lock, Long roomId) {
        try {
            if (!lock.tryLock(waitTimeout.toMillis(), TimeUnit.MILLISECONDS)) {
                throw new LockTimeoutException("방", roomId, waitTimeout);
            }
        } catch (InterruptedException e) {
            Thread.currentThread().interrupt();
            throw new IllegalStateException("방 잠금을 기다리다 인터럽트되었습니다. (roomId=" + roomId + ")", e);
        }
    }
}
