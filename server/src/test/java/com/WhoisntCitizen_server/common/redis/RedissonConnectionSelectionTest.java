package com.WhoisntCitizen_server.common.redis;

import com.WhoisntCitizen_server.game.lock.redis.GameLockRedisConfig;
import com.WhoisntCitizen_server.lobby.lock.redis.RoomLockRedisConfig;
import org.junit.jupiter.api.AfterAll;
import org.junit.jupiter.api.Test;
import org.redisson.api.RedissonClient;
import org.springframework.boot.test.context.runner.ApplicationContextRunner;
import org.testcontainers.DockerClientFactory;
import org.testcontainers.containers.GenericContainer;
import org.testcontainers.utility.DockerImageName;

import static org.assertj.core.api.Assertions.assertThat;
import static org.junit.jupiter.api.Assumptions.assumeTrue;

/**
 * 잠금 설정값(mafia.game.lock, mafia.room.lock)에 따라 그룹별 Redisson 연결이 만들어지는지 확인한다.
 *  - local이거나 값이 없으면 만들지 않는다. (Redis 없이도 서버가 뜬다)
 *  - redis면 그룹별로 하나씩 만든다. 이 경우는 실제 Redis가 필요해 Docker가 없으면 건너뛴다.
 */
class RedissonConnectionSelectionTest {

    private static GenericContainer<?> redis;

    private final ApplicationContextRunner runner = new ApplicationContextRunner()
            .withUserConfiguration(GameLockRedisConfig.class, RoomLockRedisConfig.class);

    /** redis 설정 테스트에서만 컨테이너를 띄운다. (local 테스트는 Docker 없이 돈다) */
    private static String redisHostAndPort() {
        assumeTrue(DockerClientFactory.instance().isDockerAvailable(),
                "Docker가 실행 중이 아니라 Redis 연결 생성 테스트를 건너뜁니다");
        if (redis == null) {
            redis = new GenericContainer<>(DockerImageName.parse("redis:7-alpine")).withExposedPorts(6379);
            redis.start();
        }
        return redis.getHost() + ":" + redis.getMappedPort(6379);
    }

    @AfterAll
    static void stopRedis() {
        if (redis != null) {
            redis.stop();
        }
    }

    @Test
    void 값이_없으면_Redisson_연결을_만들지_않는다() {
        runner.run(context -> assertThat(context).doesNotHaveBean(RedissonClient.class));
    }

    @Test
    void local이면_Redisson_연결을_만들지_않는다() {
        runner.withPropertyValues("mafia.game.lock=local", "mafia.room.lock=local")
                .run(context -> assertThat(context).doesNotHaveBean(RedissonClient.class));
    }

    @Test
    void 게임_잠금만_redis면_게임_그룹_연결만_만든다() {
        String[] hostPort = redisHostAndPort().split(":");
        runner.withPropertyValues("mafia.game.lock=redis",
                        "mafia.redis.game.host=" + hostPort[0], "mafia.redis.game.port=" + hostPort[1])
                .run(context -> {
                    assertThat(context).hasSingleBean(RedissonClient.class);
                    assertThat(context).hasBean("gameRedisson");
                    assertThat(context).doesNotHaveBean("lobbyRedisson");
                });
    }

    @Test
    void 둘_다_redis면_그룹별로_따로_연결을_만든다() {
        String[] hostPort = redisHostAndPort().split(":");
        runner.withPropertyValues("mafia.game.lock=redis", "mafia.room.lock=redis",
                        "mafia.redis.game.host=" + hostPort[0], "mafia.redis.game.port=" + hostPort[1],
                        "spring.data.redis.host=" + hostPort[0], "spring.data.redis.port=" + hostPort[1])
                .run(context -> {
                    assertThat(context).getBeans(RedissonClient.class).hasSize(2);
                    RedissonClient game = context.getBean("gameRedisson", RedissonClient.class);
                    RedissonClient lobby = context.getBean("lobbyRedisson", RedissonClient.class);
                    assertThat(game).isNotSameAs(lobby);
                    // 지금은 두 그룹이 같은 Redis를 바라본다
                    game.getBucket("selection-test:key").set("value");
                    assertThat(lobby.<String>getBucket("selection-test:key").get()).isEqualTo("value");
                });
    }
}
