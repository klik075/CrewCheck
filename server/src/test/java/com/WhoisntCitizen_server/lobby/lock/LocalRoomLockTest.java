package com.WhoisntCitizen_server.lobby.lock;

import com.WhoisntCitizen_server.support.LockContractTest;

import java.time.Duration;
import java.util.function.Supplier;

/** 서버 메모리 방 잠금이 잠금 계약(LockContractTest)을 지키는지 확인한다. */
class LocalRoomLockTest extends LockContractTest<RoomLock, Long> {

    @Override
    protected RoomLock newLock(Duration waitTimeout) {
        return new LocalRoomLock(waitTimeout);
    }

    @Override
    protected <T> T withLock(RoomLock lock, Long key, Supplier<T> action) {
        return lock.withLock(key, action);
    }

    @Override
    protected Long key1() {
        return 1L;
    }

    @Override
    protected Long key2() {
        return 2L;
    }
}
