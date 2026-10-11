package com.WhoisntCitizen_server.game.lock.redis;

import com.WhoisntCitizen_server.common.redis.RedissonClients;
import com.WhoisntCitizen_server.game.lock.GameLock;
import com.WhoisntCitizen_server.support.LockContractTest;
import org.junit.jupiter.api.AfterAll;
import org.junit.jupiter.api.BeforeAll;
import org.junit.jupiter.api.Test;
import org.redisson.api.RedissonClient;
import org.testcontainers.DockerClientFactory;
import org.testcontainers.containers.GenericContainer;
import org.testcontainers.utility.DockerImageName;

import java.time.Duration;
import java.util.UUID;
import java.util.function.Supplier;

import static org.assertj.core.api.Assertions.assertThat;
import static org.junit.jupiter.api.Assumptions.assumeTrue;

/**
 * RedisGameLock이 잠금 계약(LockContractTest)을 실제 Redis에서 지키는지 확인한다.
 * Docker가 꺼져 있으면 건너뛴다.
 *
 * 여기서는 서버 1대(Redisson 연결 1개)에서 여러 스레드가 경쟁하는 경우를 본다.
 * 서버 2대(연결 2개)가 경쟁하는 경우와 서버가 죽었을 때 잠금이 풀리는지는 2-6에서 확인한다.
 */
class RedisGameLockTest extends LockContractTest<GameLock, String> {

    private static GenericContainer<?> redisContainer;
    private static RedissonClient redisson;

    // 테스트마다 새 키를 쓴다. (앞 테스트에서 기다리다 포기한 잠금 등이 영향을 주지 않게)
    private final String prefix = "it-" + UUID.randomUUID() + "-";

    @BeforeAll
    static void startRedis() {
        assumeTrue(DockerClientFactory.instance().isDockerAvailable(),
                "Docker가 실행 중이 아니라 Redis 게임 잠금 테스트를 건너뜁니다");
        redisContainer = new GenericContainer<>(DockerImageName.parse("redis:7-alpine")).withExposedPorts(6379);
        redisContainer.start();
        redisson = RedissonClients.create(redisContainer.getHost(), redisContainer.getMappedPort(6379), 0, "");
    }

    @AfterAll
    static void stopRedis() {
        if (redisson != null) {
            redisson.shutdown();
        }
        if (redisContainer != null) {
            redisContainer.stop();
        }
    }

    @Override
    protected GameLock newLock(Duration waitTimeout) {
        return new RedisGameLock(redisson, waitTimeout);
    }

    @Override
    protected <T> T withLock(GameLock lock, String key, Supplier<T> action) {
        return lock.withLock(key, action);
    }

    @Override
    protected String key1() {
        return prefix + "1";
    }

    @Override
    protected String key2() {
        return prefix + "2";
    }

    // ---------- Redis 구현에만 있는 것 ----------

    @Test
    void 잠금_키는_game_lock_gameId_이고_쥔_동안만_있다() {
        GameLock lock = newLock(LONG_WAIT);
        String gameId = key1();

        lock.runWithLock(gameId, () ->
                assertThat(redisson.getKeys().countExists("game:lock:" + gameId)).isEqualTo(1));

        assertThat(redisson.getKeys().countExists("game:lock:" + gameId)).isZero();
    }

    @Test
    void 쥔_동안에는_만료_시간이_자동으로_걸려_있다() {
        // leaseTime 없이 잡으면 Redisson이 만료 시간(기본 30초)을 걸고 계속 연장한다.
        // 서버가 죽어 연장이 멈추면 이 시간이 지나 저절로 풀린다
        GameLock lock = newLock(LONG_WAIT);
        String gameId = key1();

        lock.runWithLock(gameId, () -> {
            long ttlMillis = redisson.getLock("game:lock:" + gameId).remainTimeToLive();
            assertThat(ttlMillis).isPositive().isLessThanOrEqualTo(Duration.ofSeconds(30).toMillis());
        });
    }
}
