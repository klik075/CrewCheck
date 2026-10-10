package com.WhoisntCitizen_server.lobby.lock.redis;

import com.WhoisntCitizen_server.common.exception.LockTimeoutException;
import com.WhoisntCitizen_server.game.lock.GameLockScope;
import com.WhoisntCitizen_server.lobby.lock.RoomLock;
import org.redisson.api.RLock;
import org.redisson.api.RedissonClient;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;

import java.time.Duration;
import java.util.Objects;
import java.util.concurrent.TimeUnit;
import java.util.function.Supplier;

/**
 * Redis 분산 방 잠금 (Redisson RLock). 서버가 여러 대여도 같은 방은 한 번에 한 서버의 한 스레드만 바꾼다.
 * 로비 그룹 Redis(lobbyRedisson)를 쓴다. 방 데이터(room:{id})와 같은 그룹이라 로비용 Redis를 옮기면 함께 옮겨간다.
 * 동작은 게임 잠금(RedisGameLock)과 같고, 키와 쓰는 Redis 그룹만 다르다.
 *
 * 키: room:lock:{roomId}
 *  - 로비 그룹 키는 room: 으로 시작한다. (방 데이터 room:{id}, 방 목록 rooms, 번호 발급 room:id:sequence)
 *  - 방 번호는 숫자라 room:lock:{id}가 방 데이터 키 room:{id}와 겹치지 않는다.
 *  - 잠금 키에는 보존할 데이터가 없어서 버전을 붙이지 않는다. (게임 잠금 game:lock:{id}와 같은 규칙)
 *
 * RoomLock 계약(LockContractTest)을 지키는 방법
 *  - 재진입: Redisson이 서버+스레드 단위로 잡은 횟수를 센다. (방 입장 처리 안에서 같은 방을 다시 잠그는 경우 등)
 *  - 대기 시간: tryLock(waitTimeout)으로 기다리고, 넘기면 LockTimeoutException. 작업은 실행하지 않는다.
 *  - 자동 연장(watchdog): 쥐는 시간을 정하지 않아 잡고 있는 동안 계속 연장된다. 서버가 죽으면 최대 30초 뒤 풀린다.
 *  - 예외: 작업에서 예외가 나도 finally에서 푼다. 작업의 예외를 다른 예외로 바꾸지 않는다.
 *
 * 잠금 순서: 방 잠금 안에서 게임 잠금을 잡는 것(방 → 게임)만 허용한다. 분산 잠금에서도 같은 규칙이라 교착이 생기지 않는다.
 */
public class RedisRoomLock implements RoomLock {

    static final String KEY_PREFIX = "room:lock:";

    private static final Logger log = LoggerFactory.getLogger(RedisRoomLock.class);

    private final RedissonClient redisson;
    private final Duration waitTimeout;

    public RedisRoomLock(RedissonClient redisson, Duration waitTimeout) {
        this.redisson = Objects.requireNonNull(redisson, "redisson");
        this.waitTimeout = Objects.requireNonNull(waitTimeout, "waitTimeout");
    }

    static String key(Long roomId) {
        return KEY_PREFIX + roomId;
    }

    @Override
    public <T> T withLock(Long roomId, Supplier<T> action) {
        Objects.requireNonNull(roomId, "roomId");
        GameLockScope.requireNotHeld("방", roomId); // 잠금 순서: 방 잠금 → 게임 잠금만 허용
        RLock lock = redisson.getLock(key(roomId));
        acquire(lock, roomId);
        try {
            return action.get();
        } finally {
            release(lock, roomId);
        }
    }

    private void acquire(RLock lock, Long roomId) {
        try {
            // leaseTime을 주지 않는다 → 자동 연장(watchdog)이 켜진다
            if (!lock.tryLock(waitTimeout.toMillis(), TimeUnit.MILLISECONDS)) {
                throw new LockTimeoutException("방", roomId, waitTimeout);
            }
        } catch (InterruptedException e) {
            Thread.currentThread().interrupt();
            throw new IllegalStateException("방 잠금을 기다리다 인터럽트되었습니다. (roomId=" + roomId + ")", e);
        }
    }

    private void release(RLock lock, Long roomId) {
        if (lock.isHeldByCurrentThread()) {
            lock.unlock(); // 재진입이면 횟수만 줄고, 마지막 해제 때 키가 지워진다
        } else {
            log.warn("[room {}] 작업이 끝나기 전에 방 잠금을 잃었습니다 (자동 연장 실패). 그사이 다른 서버가 같은 방을 바꿨을 수 있습니다.",
                    roomId);
        }
    }
}
