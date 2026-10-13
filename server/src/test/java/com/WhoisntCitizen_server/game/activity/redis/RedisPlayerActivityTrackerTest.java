package com.WhoisntCitizen_server.game.activity.redis;

import com.WhoisntCitizen_server.game.activity.PlayerActivityTracker;
import com.WhoisntCitizen_server.game.repository.redis.GameRedis;
import com.WhoisntCitizen_server.game.repository.redis.GameRedisProperties;
import com.WhoisntCitizen_server.support.PlayerActivityTrackerContractTest;
import org.junit.jupiter.api.AfterAll;
import org.junit.jupiter.api.BeforeAll;
import org.junit.jupiter.api.Test;
import org.springframework.data.redis.core.StringRedisTemplate;
import org.testcontainers.DockerClientFactory;
import org.testcontainers.containers.GenericContainer;
import org.testcontainers.utility.DockerImageName;

import java.time.Duration;
import java.time.Instant;
import java.util.ArrayList;
import java.util.List;
import java.util.Map;
import java.util.concurrent.CountDownLatch;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;
import java.util.concurrent.Future;
import java.util.concurrent.TimeUnit;

import static org.assertj.core.api.Assertions.assertThat;
import static org.assertj.core.api.Assertions.assertThatThrownBy;
import static org.junit.jupiter.api.Assumptions.assumeTrue;

/**
 * RedisPlayerActivityTracker를 실제 Redis로 확인한다. Docker가 꺼져 있으면 건너뛴다.
 *  - 접속 기록 계약(PlayerActivityTrackerContractTest): 서버 메모리 구현과 같은 동작을 Redis에서도 지키는지
 *  - 아래 테스트: Redis 구현에만 있는 것 (저장 형식, TTL, 서버 여러 대, 동시 기록, 잘못된 값)
 */
class RedisPlayerActivityTrackerTest extends PlayerActivityTrackerContractTest {

    private static GenericContainer<?> redisContainer;
    private static GameRedis gameRedis;

    @BeforeAll
    static void startRedis() {
        assumeTrue(DockerClientFactory.instance().isDockerAvailable(),
                "Docker가 실행 중이 아니라 Redis 접속 기록 테스트를 건너뜁니다");
        redisContainer = new GenericContainer<>(DockerImageName.parse("redis:7-alpine")).withExposedPorts(6379);
        redisContainer.start();
        gameRedis = new GameRedis(new GameRedisProperties(redisContainer.getHost(), redisContainer.getMappedPort(6379),
                0, "", 21600, 600));
    }

    @AfterAll
    static void stopRedis() {
        if (gameRedis != null) {
            gameRedis.destroy();
        }
        if (redisContainer != null) {
            redisContainer.stop();
        }
    }

    /** 테스트마다 빈 Redis에서 시작한다. (부모의 @BeforeEach가 부른다) */
    @Override
    protected PlayerActivityTracker newTracker() {
        redis().getConnectionFactory().getConnection().serverCommands().flushDb();
        return new RedisPlayerActivityTracker(gameRedis);
    }

    private static StringRedisTemplate redis() {
        return gameRedis.template();
    }

    // ---------- 저장 형식 ----------

    @Test
    void 게임마다_Hash_하나에_playerId별_epoch_밀리초로_저장한다() {
        tracker.touch("abc", 1L, NOW);
        tracker.touch("abc", 2L, NOW.plusMillis(2250));

        String key = "game:v1:activity:abc";
        assertThat(redis().type(key).code()).isEqualTo("hash");
        assertThat(redis().opsForHash().entries(key)).isEqualTo(Map.of(
                "1", Long.toString(NOW.toEpochMilli()),
                "2", Long.toString(NOW.plusMillis(2250).toEpochMilli())));
    }

    @Test
    void 밀리초보다_작은_단위는_버린다() {
        tracker.touch("g1", 1L, NOW.plusNanos(1_999_999));

        assertThat(tracker.lastSeen("g1")).isEqualTo(Map.of(1L, NOW.plusMillis(1)));
    }

    @Test
    void 지우면_키가_없어진다() {
        tracker.touch("g1", 1L, NOW);

        tracker.clear("g1");

        assertThat(redis().hasKey("game:v1:activity:g1")).isFalse();
    }

    @Test
    void 참가자가_없는_시작_기록은_키를_만들지_않는다() {
        tracker.markAllSeen("g1", List.of(), NOW);

        assertThat(redis().hasKey("game:v1:activity:g1")).isFalse();
    }

    // ---------- TTL ----------

    @Test
    void 기록하면_TTL이_걸린다() {
        PlayerActivityTracker shortTtl = new RedisPlayerActivityTracker(gameRedis, Duration.ofMinutes(5));

        shortTtl.touch("g1", 1L, NOW);

        Long ttlMillis = redis().getExpire("game:v1:activity:g1", TimeUnit.MILLISECONDS);
        assertThat(ttlMillis).isBetween(1L, Duration.ofMinutes(5).toMillis());
    }

    @Test
    void 시작_기록에도_TTL이_걸린다() {
        tracker.markAllSeen("g1", List.of(1L, 2L), NOW);

        Long ttlMillis = redis().getExpire("game:v1:activity:g1", TimeUnit.MILLISECONDS);
        assertThat(ttlMillis).isBetween(1L, RedisPlayerActivityTracker.DEFAULT_TTL.toMillis());
    }

    @Test
    void 기록할_때마다_TTL을_다시_건다() {
        String key = "game:v1:activity:g1";
        tracker.touch("g1", 1L, NOW);
        redis().expire(key, Duration.ofSeconds(10)); // 시간이 흘러 TTL이 줄어든 상황

        tracker.touch("g1", 2L, NOW.plusSeconds(1));

        assertThat(redis().getExpire(key, TimeUnit.SECONDS)).isGreaterThan(10L);
    }

    @Test
    void 이른_시각이라_반영하지_않아도_TTL은_다시_건다() {
        String key = "game:v1:activity:g1";
        tracker.touch("g1", 1L, NOW.plusSeconds(10));
        redis().expire(key, Duration.ofSeconds(10));

        tracker.touch("g1", 1L, NOW); // 값은 그대로지만 요청이 왔다는 것은 게임이 살아 있다는 뜻

        assertThat(redis().getExpire(key, TimeUnit.SECONDS)).isGreaterThan(10L);
        assertThat(tracker.lastSeen("g1")).isEqualTo(Map.of(1L, NOW.plusSeconds(10)));
    }

    @Test
    void TTL이_0_이하이면_만들_수_없다() {
        assertThatThrownBy(() -> new RedisPlayerActivityTracker(gameRedis, Duration.ZERO))
                .isInstanceOf(IllegalArgumentException.class);
    }

    // ---------- 서버 여러 대 ----------

    @Test
    void 한_서버가_기록하면_다른_서버가_본다() {
        PlayerActivityTracker serverA = new RedisPlayerActivityTracker(gameRedis);
        PlayerActivityTracker serverB = new RedisPlayerActivityTracker(gameRedis);

        serverA.markAllSeen("g1", List.of(1L, 2L), NOW);
        serverB.touch("g1", 2L, NOW.plusSeconds(5));

        assertThat(serverA.lastSeen("g1")).isEqualTo(Map.of(1L, NOW, 2L, NOW.plusSeconds(5)));
        assertThat(serverB.lastSeen("g1")).isEqualTo(serverA.lastSeen("g1"));
    }

    @Test
    void 여러_서버가_동시에_기록해도_가장_늦은_시각이_남는다() throws Exception {
        int servers = 4;
        int touchesPerServer = 200;
        ExecutorService pool = Executors.newFixedThreadPool(servers);
        CountDownLatch start = new CountDownLatch(1);
        try {
            List<Future<?>> futures = new ArrayList<>();
            for (int s = 0; s < servers; s++) {
                int serverNo = s;
                PlayerActivityTracker server = new RedisPlayerActivityTracker(gameRedis);
                futures.add(pool.submit(() -> {
                    start.await();
                    // 서버마다 시각이 뒤섞여 도착한다: 0, 4, 8, ... / 1, 5, 9, ... 를 거꾸로 보낸다
                    for (int i = touchesPerServer - 1; i >= 0; i--) {
                        server.touch("g1", 1L, NOW.plusMillis((long) i * servers + serverNo));
                    }
                    return null;
                }));
            }
            start.countDown();
            for (Future<?> f : futures) {
                f.get(30, TimeUnit.SECONDS);
            }
        } finally {
            pool.shutdownNow();
        }

        long latest = (long) (touchesPerServer - 1) * servers + (servers - 1);
        assertThat(tracker.lastSeen("g1")).isEqualTo(Map.of(1L, NOW.plusMillis(latest)));
    }

    // ---------- 잘못된 값 ----------

    @Test
    void 형식이_잘못된_값은_건너뛰고_나머지를_돌려준다() {
        tracker.touch("g1", 1L, NOW);
        redis().opsForHash().put("game:v1:activity:g1", "2", "not-a-number");
        redis().opsForHash().put("game:v1:activity:g1", "player-3", Long.toString(NOW.toEpochMilli()));

        assertThat(tracker.lastSeen("g1")).isEqualTo(Map.of(1L, NOW));
    }
}
