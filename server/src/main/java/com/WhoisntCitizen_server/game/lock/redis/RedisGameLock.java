package com.WhoisntCitizen_server.game.lock.redis;

import com.WhoisntCitizen_server.common.exception.LockTimeoutException;
import com.WhoisntCitizen_server.game.lock.GameLock;
import org.redisson.api.RLock;
import org.redisson.api.RedissonClient;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;

import java.time.Duration;
import java.util.Objects;
import java.util.concurrent.TimeUnit;
import java.util.function.Supplier;

/**
 * Redis 분산 게임 잠금 (Redisson RLock). 서버가 여러 대여도 같은 게임은 한 번에 한 서버의 한 스레드만 바꾼다.
 * 게임 그룹 Redis(gameRedisson)를 쓴다. 게임 상태와 같은 그룹이라 게임용 Redis를 옮기면 함께 옮겨간다.
 *
 * 키: game:lock:{gameId}
 *  - 잠금 키에는 보존할 데이터가 없어서 데이터 키(game:v1:state:...)와 달리 버전(v1)을 붙이지 않는다.
 *  - Redisson은 이 키에 "누가(서버+스레드) 몇 번 잡았는지"를 해시로 적고, 풀리면 지운다.
 *    기다리는 쪽을 깨우는 알림 채널(redisson_lock__channel:{키})도 Redisson이 따로 쓴다.
 *
 * GameLock 계약(LockContractTest)을 지키는 방법
 *  - 재진입: Redisson이 서버+스레드 단위로 잡은 횟수를 센다. 같은 스레드가 다시 잡으면 횟수만 늘고, 다 풀어야 풀린다.
 *  - 대기 시간: tryLock(waitTimeout)으로 기다리고, 넘기면 LockTimeoutException. 작업은 실행하지 않는다.
 *  - 자동 연장(watchdog): 쥐는 시간(leaseTime)을 정하지 않으면 Redisson이 잡고 있는 동안 만료 시간을 계속 늘린다
 *    (기본 30초, 10초마다 연장). 서버가 죽어서 연장이 멈추면 최대 30초 뒤 잠금이 저절로 풀려 다른 서버가 이어받는다.
 *  - 예외: 작업에서 예외가 나도 finally에서 푼다. 작업의 예외를 다른 예외로 바꾸지 않는다.
 *
 * 잠금을 잃는 경우: Redis 장애 전환이나 아주 긴 멈춤(GC 등)으로 연장이 끊기면, 작업이 끝나기 전에 잠금이 풀릴 수 있다.
 * 그때 unlock은 실패하므로, 풀기 전에 아직 쥐고 있는지 확인하고 아니면 경고만 남긴다. (작업의 결과·예외를 덮지 않기 위해)
 * 이 경우까지 막으려면 저장할 때 버전을 확인하는 안전장치가 필요하다. (2-9, 선택)
 */
public class RedisGameLock implements GameLock {

    static final String KEY_PREFIX = "game:lock:";

    private static final Logger log = LoggerFactory.getLogger(RedisGameLock.class);

    private final RedissonClient redisson;
    private final Duration waitTimeout;

    public RedisGameLock(RedissonClient redisson, Duration waitTimeout) {
        this.redisson = Objects.requireNonNull(redisson, "redisson");
        this.waitTimeout = Objects.requireNonNull(waitTimeout, "waitTimeout");
    }

    static String key(String gameId) {
        return KEY_PREFIX + gameId;
    }

    @Override
    public <T> T withLock(String gameId, Supplier<T> action) {
        Objects.requireNonNull(gameId, "gameId");
        RLock lock = redisson.getLock(key(gameId));
        acquire(lock, gameId);
        try {
            return action.get();
        } finally {
            release(lock, gameId);
        }
    }

    private void acquire(RLock lock, String gameId) {
        try {
            // leaseTime을 주지 않는다 → 자동 연장(watchdog)이 켜진다
            if (!lock.tryLock(waitTimeout.toMillis(), TimeUnit.MILLISECONDS)) {
                throw new LockTimeoutException("게임", gameId, waitTimeout);
            }
        } catch (InterruptedException e) {
            Thread.currentThread().interrupt(); // 인터럽트 표시를 되살려 호출한 쪽이 알 수 있게 한다
            throw new IllegalStateException("게임 잠금을 기다리다 인터럽트되었습니다. (gameId=" + gameId + ")", e);
        }
    }

    private void release(RLock lock, String gameId) {
        if (lock.isHeldByCurrentThread()) {
            lock.unlock(); // 재진입이면 횟수만 줄고, 마지막 해제 때 키가 지워진다
        } else {
            log.warn("[{}] 작업이 끝나기 전에 게임 잠금을 잃었습니다 (자동 연장 실패). 그사이 다른 서버가 같은 게임을 바꿨을 수 있습니다.",
                    gameId);
        }
    }
}
