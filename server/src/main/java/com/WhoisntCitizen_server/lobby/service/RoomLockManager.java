package com.WhoisntCitizen_server.lobby.service;

import org.springframework.stereotype.Component;

import java.util.concurrent.ConcurrentHashMap;
import java.util.function.Supplier;

/**
 * 방 번호별 잠금.
 * Redis에서 방을 읽고 → Java에서 수정하고 → 다시 저장하는 작업(입장, 나가기, 게임 시작/종료)이
 * 같은 방에 동시에 들어오면 한쪽의 변경이 덮어써질 수 있다(lost update).
 * 같은 roomId에 대한 작업은 한 번에 하나씩만 실행되도록 이 클래스로 감싼다.
 * 다른 방끼리는 서로 기다리지 않는다.
 *
 * 주의: 서버 1대 기준(JVM 메모리 안의 잠금)이다. 서버를 여러 대로 늘리면 Redis 분산 락(Redisson RLock 등)으로 교체한다.
 */
@Component
public class RoomLockManager {

    private final ConcurrentHashMap<Long, Object> locks = new ConcurrentHashMap<>();

    /** roomId 잠금을 잡은 상태에서 action을 실행하고 결과를 반환한다. */
    public <T> T withLock(Long roomId, Supplier<T> action) {
        Object lock = locks.computeIfAbsent(roomId, id -> new Object());
        synchronized (lock) {
            return action.get();
        }
    }

    /** 반환값이 없는 작업용. */
    public void withLock(Long roomId, Runnable action) {
        withLock(roomId, () -> {
            action.run();
            return null;
        });
    }
}
