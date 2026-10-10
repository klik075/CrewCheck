package com.WhoisntCitizen_server.lobby.lock.redis;

import com.WhoisntCitizen_server.common.redis.RedissonClients;
import com.WhoisntCitizen_server.lobby.lock.RoomLock;
import com.WhoisntCitizen_server.support.LockContractTest;
import org.junit.jupiter.api.AfterAll;
import org.junit.jupiter.api.BeforeAll;
import org.junit.jupiter.api.Test;
import org.redisson.api.RedissonClient;
import org.testcontainers.DockerClientFactory;
import org.testcontainers.containers.GenericContainer;
import org.testcontainers.utility.DockerImageName;

import java.time.Duration;
import java.util.concurrent.atomic.AtomicLong;
import java.util.function.Supplier;

import static org.assertj.core.api.Assertions.assertThat;
import static org.junit.jupiter.api.Assumptions.assumeTrue;

/**
 * RedisRoomLock이 잠금 계약(LockContractTest)을 실제 Redis에서 지키는지 확인한다.
 * Docker가 꺼져 있으면 건너뛴다. 서버 2대 경쟁·서버 다운은 2-6에서 확인한다.
 */
class RedisRoomLockTest extends LockContractTest<RoomLock, Long> {

    private static GenericContainer<?> redisContainer;
    private static RedissonClient redisson;

    // 테스트마다 새 방 번호를 쓴다. (앞 테스트의 잠금이 영향을 주지 않게)
    private static final AtomicLong NEXT_ROOM_ID = new AtomicLong(1_000_000);
    private final long room1 = NEXT_ROOM_ID.getAndAdd(2);
    private final long room2 = room1 + 1;

    @BeforeAll
    static void startRedis() {
        assumeTrue(DockerClientFactory.instance().isDockerAvailable(),
                "Docker가 실행 중이 아니라 Redis 방 잠금 테스트를 건너뜁니다");
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
    protected RoomLock newLock(Duration waitTimeout) {
        return new RedisRoomLock(redisson, waitTimeout);
    }

    @Override
    protected <T> T withLock(RoomLock lock, Long key, Supplier<T> action) {
        return lock.withLock(key, action);
    }

    @Override
    protected Long key1() {
        return room1;
    }

    @Override
    protected Long key2() {
        return room2;
    }

    // ---------- Redis 구현에만 있는 것 ----------

    @Test
    void 잠금_키는_room_lock_roomId_이고_쥔_동안만_있다() {
        RoomLock lock = newLock(LONG_WAIT);

        lock.runWithLock(room1, () ->
                assertThat(redisson.getKeys().countExists("room:lock:" + room1)).isEqualTo(1));

        assertThat(redisson.getKeys().countExists("room:lock:" + room1)).isZero();
    }

    @Test
    void 잠금_키는_방_데이터_키와_겹치지_않는다() {
        // 방 데이터는 room:{id}에 있다 (LobbyRoomRepository). 잠금을 잡아도 그 키는 생기지 않는다
        RoomLock lock = newLock(LONG_WAIT);

        lock.runWithLock(room1, () ->
                assertThat(redisson.getKeys().countExists("room:" + room1)).isZero());
    }

    @Test
    void 쥔_동안에는_만료_시간이_자동으로_걸려_있다() {
        RoomLock lock = newLock(LONG_WAIT);

        lock.runWithLock(room1, () -> {
            long ttlMillis = redisson.getLock("room:lock:" + room1).remainTimeToLive();
            assertThat(ttlMillis).isPositive().isLessThanOrEqualTo(Duration.ofSeconds(30).toMillis());
        });
    }
}
