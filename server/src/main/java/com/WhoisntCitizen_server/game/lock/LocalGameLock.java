package com.WhoisntCitizen_server.game.lock;

import java.util.Objects;
import java.util.concurrent.ConcurrentHashMap;
import java.util.concurrent.locks.ReentrantLock;
import java.util.function.Supplier;

/**
 * 서버 메모리(JVM) 안의 게임 잠금. 서버 1대 기준이며, 여러 대로 늘리면 Redis 분산 락으로 바꾼다.
 * 예전의 synchronized(game)과 같은 동작이다. (게임 객체가 gameId당 하나라서 잠금 대상이 같다)
 * ReentrantLock을 쓰는 이유: synchronized처럼 재진입이 되고, 나중에 tryLock(대기 시간 제한)으로 바꾸기 쉽다.
 *
 * 끝난 게임의 잠금 객체는 지우지 않는다. 다른 스레드가 기다리는 중일 수 있어 안전하게 지우기 어렵고,
 * 게임 하나당 작은 객체 하나라 지금 규모에서는 문제가 되지 않는다.
 */
public class LocalGameLock implements GameLock {

    private final ConcurrentHashMap<String, ReentrantLock> locks = new ConcurrentHashMap<>();

    @Override
    public <T> T withLock(String gameId, Supplier<T> action) {
        Objects.requireNonNull(gameId, "gameId");
        ReentrantLock lock = locks.computeIfAbsent(gameId, id -> new ReentrantLock());
        lock.lock();
        try {
            return action.get();
        } finally {
            lock.unlock();
        }
    }
}
