package com.WhoisntCitizen_server.common.redis;

import com.WhoisntCitizen_server.game.lock.GameLock;
import com.WhoisntCitizen_server.game.lock.GameLockConfig;
import com.WhoisntCitizen_server.game.lock.LocalGameLock;
import com.WhoisntCitizen_server.game.lock.redis.GameLockRedisConfig;
import com.WhoisntCitizen_server.game.lock.redis.RedisGameLock;
import com.WhoisntCitizen_server.lobby.config.RoomLockConfig;
import com.WhoisntCitizen_server.lobby.lock.LocalRoomLock;
import com.WhoisntCitizen_server.lobby.lock.RoomLock;
import com.WhoisntCitizen_server.lobby.lock.redis.RedisRoomLock;
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
 * 2-7: 설정값(mafia.game.lock, mafia.room.lock)에 따라 잠금 구현이 하나씩만 등록되는지 확인한다.
 *  - local이거나 값이 없으면 서버 메모리 잠금 (Docker 없이 확인)
 *  - redis면 Redis 분산 잠금 (Redisson이 만들 때 바로 접속하므로 Docker가 없으면 건너뜀)
 * 게임 잠금과 방 잠금은 따로 고를 수 있다.
 */
class LockSelectionTest {

    private static GenericContainer<?> redis;

    private final ApplicationContextRunner runner = new ApplicationContextRunner()
            .withUserConfiguration(GameLockConfig.class, GameLockRedisConfig.class,
                    RoomLockConfig.class, RoomLockRedisConfig.class);

    private static String[] redisHostPort() {
        assumeTrue(DockerClientFactory.instance().isDockerAvailable(),
                "Docker가 실행 중이 아니라 Redis 잠금 선택 테스트를 건너뜁니다");
        if (redis == null) {
            redis = new GenericContainer<>(DockerImageName.parse("redis:7-alpine")).withExposedPorts(6379);
            redis.start();
        }
        return new String[]{redis.getHost(), String.valueOf(redis.getMappedPort(6379))};
    }

    @AfterAll
    static void stopRedis() {
        if (redis != null) {
            redis.stop();
        }
    }

    @Test
    void 값이_없으면_둘_다_서버_메모리_잠금이다() {
        runner.run(context -> {
            assertThat(context).hasSingleBean(GameLock.class);
            assertThat(context.getBean(GameLock.class)).isInstanceOf(LocalGameLock.class);
            assertThat(context).hasSingleBean(RoomLock.class);
            assertThat(context.getBean(RoomLock.class)).isInstanceOf(LocalRoomLock.class);
            assertThat(context).doesNotHaveBean(RedissonClient.class);   // Redis에 접속하지 않음
        });
    }

    @Test
    void local이면_둘_다_서버_메모리_잠금이다() {
        runner.withPropertyValues("mafia.game.lock=local", "mafia.room.lock=local").run(context -> {
            assertThat(context.getBean(GameLock.class)).isInstanceOf(LocalGameLock.class);
            assertThat(context.getBean(RoomLock.class)).isInstanceOf(LocalRoomLock.class);
            assertThat(context).doesNotHaveBean(RedissonClient.class);
        });
    }

    @Test
    void 둘_다_redis면_둘_다_Redis_분산_잠금이다() {
        String[] hp = redisHostPort();
        runner.withPropertyValues("mafia.game.lock=redis", "mafia.room.lock=redis",
                        "mafia.redis.game.host=" + hp[0], "mafia.redis.game.port=" + hp[1],
                        "spring.data.redis.host=" + hp[0], "spring.data.redis.port=" + hp[1])
                .run(context -> {
                    assertThat(context).hasSingleBean(GameLock.class);
                    assertThat(context.getBean(GameLock.class)).isInstanceOf(RedisGameLock.class);
                    assertThat(context).hasSingleBean(RoomLock.class);
                    assertThat(context.getBean(RoomLock.class)).isInstanceOf(RedisRoomLock.class);
                    // 실제로 동작하는지 한 번 잡아 본다
                    assertThat(context.getBean(GameLock.class).withLock("selection-game", () -> "ok")).isEqualTo("ok");
                    assertThat(context.getBean(RoomLock.class).withLock(1L, () -> "ok")).isEqualTo("ok");
                });
    }

    @Test
    void 게임_잠금과_방_잠금은_따로_고를_수_있다() {
        String[] hp = redisHostPort();
        runner.withPropertyValues("mafia.game.lock=redis", "mafia.room.lock=local",
                        "mafia.redis.game.host=" + hp[0], "mafia.redis.game.port=" + hp[1])
                .run(context -> {
                    assertThat(context.getBean(GameLock.class)).isInstanceOf(RedisGameLock.class);
                    assertThat(context.getBean(RoomLock.class)).isInstanceOf(LocalRoomLock.class);
                    assertThat(context).hasBean("gameRedisson").doesNotHaveBean("lobbyRedisson");
                });
    }
}
