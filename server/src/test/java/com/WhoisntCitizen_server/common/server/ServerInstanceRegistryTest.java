package com.WhoisntCitizen_server.common.server;

import com.WhoisntCitizen_server.common.server.ServerInstanceRegistry.ClaimResult;
import com.WhoisntCitizen_server.support.ManualTaskScheduler;
import com.WhoisntCitizen_server.support.MutableClock;
import org.junit.jupiter.api.AfterAll;
import org.junit.jupiter.api.BeforeAll;
import org.junit.jupiter.api.BeforeEach;
import org.junit.jupiter.api.Test;
import org.springframework.data.redis.connection.RedisStandaloneConfiguration;
import org.springframework.data.redis.connection.lettuce.LettuceConnectionFactory;
import org.springframework.data.redis.core.StringRedisTemplate;
import org.testcontainers.DockerClientFactory;
import org.testcontainers.containers.GenericContainer;
import org.testcontainers.utility.DockerImageName;

import java.time.Instant;

import static org.assertj.core.api.Assertions.assertThat;
import static org.junit.jupiter.api.Assumptions.assumeTrue;

/**
 * 같은 서버 ID를 쓰는 프로세스가 둘이면 알아채는지 확인한다. Docker가 꺼져 있으면 건너뛴다.
 * 주기 연장은 직접 refresh()를 불러 확인한다. (스케줄러는 쓰지 않음)
 */
class ServerInstanceRegistryTest {

    private static GenericContainer<?> redisContainer;
    private static LettuceConnectionFactory connectionFactory;
    private static StringRedisTemplate redis;

    private final ManualTaskScheduler scheduler = new ManualTaskScheduler(new MutableClock(Instant.parse("2026-10-18T00:00:00Z")));

    @BeforeAll
    static void startRedis() {
        assumeTrue(DockerClientFactory.instance().isDockerAvailable(),
                "Docker가 실행 중이 아니라 서버 ID 중복 확인 테스트를 건너뜁니다");
        redisContainer = new GenericContainer<>(DockerImageName.parse("redis:7-alpine")).withExposedPorts(6379);
        redisContainer.start();
        connectionFactory = new LettuceConnectionFactory(
                new RedisStandaloneConfiguration(redisContainer.getHost(), redisContainer.getMappedPort(6379)));
        connectionFactory.afterPropertiesSet();
        redis = new StringRedisTemplate(connectionFactory);
    }

    @AfterAll
    static void stopRedis() {
        if (connectionFactory != null) {
            connectionFactory.destroy();
        }
        if (redisContainer != null) {
            redisContainer.stop();
        }
    }

    @BeforeEach
    void flush() {
        redis.getConnectionFactory().getConnection().serverCommands().flushDb();
    }

    private ServerInstanceRegistry registry(ServerIdentity identity) {
        return new ServerInstanceRegistry(identity, redis, scheduler);
    }

    private static String key(String id) {
        return ServerInstanceRegistry.KEY_PREFIX + id;
    }

    @Test
    void 처음_켜면_내_토큰을_TTL과_함께_적는다() {
        ServerIdentity a = ServerIdentity.resolve("server-a");

        assertThat(registry(a).claimOnStart()).isEqualTo(ClaimResult.CLAIMED);
        assertThat(redis.opsForValue().get(key("server-a"))).isEqualTo(a.instanceToken());
        assertThat(redis.getExpire(key("server-a"))).isPositive();
    }

    @Test
    void ID가_다르면_서로_충돌하지_않는다() {
        assertThat(registry(ServerIdentity.resolve("server-a")).claimOnStart()).isEqualTo(ClaimResult.CLAIMED);
        assertThat(registry(ServerIdentity.resolve("server-b")).claimOnStart()).isEqualTo(ClaimResult.CLAIMED);
    }

    @Test
    void 같은_ID로_두_번째_프로세스가_켜지면_충돌로_알린다() {
        ServerInstanceRegistry first = registry(ServerIdentity.resolve("server-a"));
        ServerInstanceRegistry second = registry(ServerIdentity.resolve("server-a"));
        first.claimOnStart();

        assertThat(second.claimOnStart()).isEqualTo(ClaimResult.CONFLICT);
        // 먼저 켜진 쪽도 다음 연장 때 남이 차지한 것을 알아챈다
        assertThat(first.refresh()).isEqualTo(ClaimResult.CONFLICT);
    }

    @Test
    void 내_것이면_연장하고_키가_사라졌으면_다시_적는다() {
        ServerIdentity a = ServerIdentity.resolve("server-a");
        ServerInstanceRegistry registry = registry(a);
        registry.claimOnStart();

        assertThat(registry.refresh()).isEqualTo(ClaimResult.REFRESHED);

        redis.delete(key("server-a")); // Redis 재시작 등으로 키가 없어짐
        assertThat(registry.refresh()).isEqualTo(ClaimResult.CLAIMED);
        assertThat(redis.opsForValue().get(key("server-a"))).isEqualTo(a.instanceToken());
    }

    @Test
    void 끌_때는_내_키만_지운다() {
        ServerInstanceRegistry first = registry(ServerIdentity.resolve("server-a"));
        ServerIdentity secondId = ServerIdentity.resolve("server-a");
        ServerInstanceRegistry second = registry(secondId);
        first.claimOnStart();
        second.claimOnStart(); // second가 덮어씀

        first.release(); // first는 자기 것이 아니므로 지우지 않는다
        assertThat(redis.opsForValue().get(key("server-a"))).isEqualTo(secondId.instanceToken());

        second.release();
        assertThat(redis.hasKey(key("server-a"))).isFalse();
    }
}
