package com.WhoisntCitizen_server.game.lock;

import com.WhoisntCitizen_server.support.LockContractTest;

import java.time.Duration;
import java.util.function.Supplier;

/** 서버 메모리 게임 잠금이 잠금 계약(LockContractTest)을 지키는지 확인한다. */
class LocalGameLockTest extends LockContractTest<GameLock, String> {

    @Override
    protected GameLock newLock(Duration waitTimeout) {
        return new LocalGameLock(waitTimeout);
    }

    @Override
    protected <T> T withLock(GameLock lock, String key, Supplier<T> action) {
        return lock.withLock(key, action);
    }

    @Override
    protected String key1() {
        return "game-1";
    }

    @Override
    protected String key2() {
        return "game-2";
    }
}
