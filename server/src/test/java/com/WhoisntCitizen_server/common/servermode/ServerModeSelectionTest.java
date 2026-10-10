package com.WhoisntCitizen_server.common.servermode;

import com.WhoisntCitizen_server.game.lock.GameLock;
import com.WhoisntCitizen_server.game.lock.GameLockConfig;
import com.WhoisntCitizen_server.game.lock.LocalGameLock;
import com.WhoisntCitizen_server.game.lock.redis.GameLockRedisConfig;
import com.WhoisntCitizen_server.game.lock.redis.RedisGameLock;
import com.WhoisntCitizen_server.game.repository.GameRepository;
import com.WhoisntCitizen_server.game.repository.InMemoryGameRepository;
import com.WhoisntCitizen_server.game.repository.redis.GameRedis;
import com.WhoisntCitizen_server.game.repository.redis.GameRedisConfig;
import com.WhoisntCitizen_server.game.repository.redis.GameRedisConnectionConfig;
import com.WhoisntCitizen_server.game.repository.redis.RedisGameRepository;
import com.WhoisntCitizen_server.game.scheduling.GameTimer;
import com.WhoisntCitizen_server.game.scheduling.GameTimerConfig;
import com.WhoisntCitizen_server.game.scheduling.LocalGameTimer;
import com.WhoisntCitizen_server.game.scheduling.redis.GameTimerRedisConfig;
import com.WhoisntCitizen_server.game.scheduling.redis.RedisGameTimer;
import com.WhoisntCitizen_server.game.scheduling.redis.RedisTimerDispatcher;
import com.WhoisntCitizen_server.game.scheduling.redis.RedisTimerPoller;
import com.WhoisntCitizen_server.lobby.config.RoomLockConfig;
import com.WhoisntCitizen_server.lobby.lock.LocalRoomLock;
import com.WhoisntCitizen_server.lobby.lock.RoomLock;
import com.WhoisntCitizen_server.lobby.lock.redis.RedisRoomLock;
import com.WhoisntCitizen_server.lobby.lock.redis.RoomLockRedisConfig;
import org.junit.jupiter.api.AfterAll;
import com.WhoisntCitizen_server.support.ManualTaskScheduler;
import com.WhoisntCitizen_server.support.MutableClock;
import org.junit.jupiter.api.Test;
import org.redisson.api.RedissonClient;
import org.springframework.boot.test.context.runner.ApplicationContextRunner;
import org.springframework.scheduling.TaskScheduler;
import org.testcontainers.DockerClientFactory;
import org.testcontainers.containers.GenericContainer;
import org.testcontainers.utility.DockerImageName;

import java.time.Clock;
import java.time.Instant;

import static org.assertj.core.api.Assertions.assertThat;
import static org.junit.jupiter.api.Assumptions.assumeTrue;

/**
 * mafia.server.mode 하나로 게임 저장소·게임 잠금·방 잠금이 함께 바뀌는지, 개별 설정으로 덮어쓸 수 있는지 확인한다.
 * Redisson은 만들 때 바로 Redis에 접속하므로 Redis 잠금이 만들어지는 경우만 Docker가 필요하다. (없으면 건너뜀)
 * 게임용 Redis 연결(GameRedis)은 처음 명령을 보낼 때 접속하므로 Docker 없이 확인할 수 있다.
 */
class ServerModeSelectionTest {

    private static GenericContainer<?> redis;

    private final ApplicationContextRunner runner = new ApplicationContextRunner()
            .withUserConfiguration(InMemoryGameRepository.class, GameRedisConfig.class, GameRedisConnectionConfig.class,
                    GameLockConfig.class, GameLockRedisConfig.class,
                    RoomLockConfig.class, RoomLockRedisConfig.class,
                    GameTimerConfig.class, GameTimerRedisConfig.class)
            .withBean(Clock.class, Clock::systemUTC)
            // 타이머가 쓰는 공용 스케줄러. 직접 돌리지 않는 가짜라 Redis 타이머 확인 작업이 켜져도 Redis에 묻지 않는다
            .withBean("gamePhaseScheduler", TaskScheduler.class,
                    () -> new ManualTaskScheduler(new MutableClock(Instant.parse("2026-10-11T12:00:00Z"))));

    private static String[] redisProperties() {
        assumeTrue(DockerClientFactory.instance().isDockerAvailable(),
                "Docker가 실행 중이 아니라 multi 모드 Redis 잠금 확인을 건너뜁니다");
        if (redis == null) {
            redis = new GenericContainer<>(DockerImageName.parse("redis:7-alpine")).withExposedPorts(6379);
            redis.start();
        }
        String host = redis.getHost();
        String port = String.valueOf(redis.getMappedPort(6379));
        return new String[]{"mafia.redis.game.host=" + host, "mafia.redis.game.port=" + port,
                "spring.data.redis.host=" + host, "spring.data.redis.port=" + port};
    }

    @AfterAll
    static void stopRedis() {
        if (redis != null) {
            redis.stop();
        }
    }

    @Test
    void single이면_전부_서버_메모리이고_Redis에_접속하지_않는다() {
        runner.withPropertyValues("mafia.server.mode=single").run(context -> {
            assertThat(context.getBean(GameRepository.class)).isInstanceOf(InMemoryGameRepository.class);
            assertThat(context.getBean(GameLock.class)).isInstanceOf(LocalGameLock.class);
            assertThat(context.getBean(RoomLock.class)).isInstanceOf(LocalRoomLock.class);
            assertThat(context.getBean(GameTimer.class)).isInstanceOf(LocalGameTimer.class);
            assertThat(context).doesNotHaveBean(GameRedis.class).doesNotHaveBean(RedissonClient.class)
                    .doesNotHaveBean(RedisTimerPoller.class);
        });
    }

    @Test
    void 개별_설정이_빈_문자열이면_모드를_따른다() {
        // application.properties의 ${GAME_REPOSITORY:} 등은 환경변수가 없으면 빈 문자열이 된다
        runner.withPropertyValues("mafia.server.mode=single",
                        "mafia.game.repository=", "mafia.game.lock=", "mafia.room.lock=", "mafia.game.timer=")
                .run(context -> {
                    assertThat(context.getBean(GameRepository.class)).isInstanceOf(InMemoryGameRepository.class);
                    assertThat(context.getBean(GameLock.class)).isInstanceOf(LocalGameLock.class);
                    assertThat(context.getBean(RoomLock.class)).isInstanceOf(LocalRoomLock.class);
                });
    }

    @Test
    void single에서_저장소만_redis로_덮어쓸_수_있다() {
        runner.withPropertyValues("mafia.server.mode=single", "mafia.game.repository=redis").run(context -> {
            assertThat(context).hasSingleBean(GameRepository.class);
            assertThat(context.getBean(GameRepository.class)).isInstanceOf(RedisGameRepository.class);
            assertThat(context.getBean(GameLock.class)).isInstanceOf(LocalGameLock.class);
            assertThat(context.getBean(RoomLock.class)).isInstanceOf(LocalRoomLock.class);
        });
    }

    @Test
    void multi에서_잠금을_local로_덮어쓰면_잠금만_서버_메모리다() {
        runner.withPropertyValues("mafia.server.mode=multi", "mafia.game.lock=local", "mafia.room.lock=local")
                .run(context -> {
                    assertThat(context.getBean(GameRepository.class)).isInstanceOf(RedisGameRepository.class);
                    assertThat(context.getBean(GameLock.class)).isInstanceOf(LocalGameLock.class);
                    assertThat(context.getBean(RoomLock.class)).isInstanceOf(LocalRoomLock.class);
                    assertThat(context.getBean(GameTimer.class)).isInstanceOf(RedisGameTimer.class);
                    assertThat(context).doesNotHaveBean(RedissonClient.class);
                });
    }

    @Test
    void single에서_타이머만_redis로_덮어쓰면_게임용_Redis_연결도_만든다() {
        runner.withPropertyValues("mafia.server.mode=single", "mafia.game.timer=redis").run(context -> {
            assertThat(context.getBean(GameRepository.class)).isInstanceOf(InMemoryGameRepository.class);
            assertThat(context).hasSingleBean(GameTimer.class);
            assertThat(context.getBean(GameTimer.class)).isInstanceOf(RedisGameTimer.class);
            assertThat(context).hasSingleBean(GameRedis.class)
                    .hasSingleBean(RedisTimerDispatcher.class)
                    .hasSingleBean(RedisTimerPoller.class);
            assertThat(context.getBean(RedisTimerPoller.class).isRunning()).as("서버가 켜지면 확인 작업도 켜진다").isTrue();
        });
    }

    @Test
    void 저장소와_타이머가_모두_redis여도_게임용_Redis_연결은_하나다() {
        runner.withPropertyValues("mafia.game.repository=redis", "mafia.game.timer=redis").run(context ->
                assertThat(context).hasSingleBean(GameRedis.class));
    }

    @Test
    void multi에서_타이머를_local로_덮어쓰면_서버_메모리_타이머를_쓴다() {
        runner.withPropertyValues("mafia.server.mode=multi", "mafia.game.lock=local", "mafia.room.lock=local",
                        "mafia.game.timer=local")
                .run(context -> {
                    assertThat(context.getBean(GameTimer.class)).isInstanceOf(LocalGameTimer.class);
                    assertThat(context).doesNotHaveBean(RedisTimerPoller.class).doesNotHaveBean(RedisTimerDispatcher.class);
                    assertThat(context).hasSingleBean(GameRedis.class);   // 저장소는 여전히 redis
                });
    }

    @Test
    void multi면_전부_redis다() {
        runner.withPropertyValues("mafia.server.mode=multi").withPropertyValues(redisProperties()).run(context -> {
            assertThat(context).hasSingleBean(GameRepository.class);
            assertThat(context.getBean(GameRepository.class)).isInstanceOf(RedisGameRepository.class);
            assertThat(context).hasSingleBean(GameLock.class);
            assertThat(context.getBean(GameLock.class)).isInstanceOf(RedisGameLock.class);
            assertThat(context).hasSingleBean(RoomLock.class);
            assertThat(context.getBean(RoomLock.class)).isInstanceOf(RedisRoomLock.class);
            assertThat(context.getBean(GameLock.class).withLock("mode-game", () -> "ok")).isEqualTo("ok");
            assertThat(context.getBean(RoomLock.class).withLock(1L, () -> "ok")).isEqualTo("ok");
            assertThat(context).hasSingleBean(GameTimer.class);
            assertThat(context.getBean(GameTimer.class)).isInstanceOf(RedisGameTimer.class);
        });
    }

    @Test
    void 잘못된_값이면_서버가_뜨지_않고_어느_설정인지_알려준다() {
        runner.withPropertyValues("mafia.game.lock=redsi").run(context -> {
            assertThat(context).hasFailed();
            assertThat(context.getStartupFailure()).rootCause()
                    .isInstanceOf(IllegalStateException.class)
                    .hasMessageContaining("mafia.game.lock")
                    .hasMessageContaining("redsi");
        });
    }
}
