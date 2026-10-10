package com.WhoisntCitizen_server.game.scheduling.redis;

import com.WhoisntCitizen_server.game.repository.redis.GameRedis;
import com.WhoisntCitizen_server.game.repository.redis.GameRedisProperties;
import com.WhoisntCitizen_server.game.scheduling.TimerKeys;
import org.junit.jupiter.api.AfterAll;
import org.junit.jupiter.api.BeforeAll;
import org.junit.jupiter.api.BeforeEach;
import org.junit.jupiter.api.Test;
import org.testcontainers.DockerClientFactory;
import org.testcontainers.containers.GenericContainer;
import org.testcontainers.utility.DockerImageName;

import java.time.Instant;

import static org.assertj.core.api.Assertions.assertThat;
import static org.junit.jupiter.api.Assumptions.assumeTrue;

/**
 * 3-2: RedisGameTimer의 예약 부분을 실제 Redis로 확인한다. Docker가 꺼져 있으면 건너뛴다.
 * 시간이 된 예약을 실행하는 부분은 3-3에서 추가하고, 그때 타이머 계약 테스트(GameTimerContractTest)도 상속한다.
 */
class RedisGameTimerTest {

    private static final Instant NOW = Instant.parse("2026-10-10T12:00:00Z");

    private static GenericContainer<?> redisContainer;
    private static GameRedis gameRedis;

    private RedisGameTimer timer;

    @BeforeAll
    static void startRedis() {
        assumeTrue(DockerClientFactory.instance().isDockerAvailable(),
                "Docker가 실행 중이 아니라 Redis 타이머 테스트를 건너뜁니다");
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

    @BeforeEach
    void setUp() {
        gameRedis.template().getConnectionFactory().getConnection().serverCommands().flushDb();
        timer = new RedisGameTimer(gameRedis);
    }

    @Test
    void 페이즈_타이머는_대상_이름을_member로_시각을_점수로_저장한다() {
        timer.schedulePhaseTimeout("g1", 3, NOW.plusSeconds(30));

        Double score = gameRedis.template().opsForZSet().score("game:v1:timers", "phase:g1:3");
        assertThat(score).isEqualTo((double) NOW.plusSeconds(30).toEpochMilli());
    }

    @Test
    void 정리_타이머도_같은_곳에_저장한다() {
        timer.scheduleCleanup("g1", NOW.plusSeconds(60));

        assertThat(timer.scheduledAt(TimerKeys.cleanup("g1"))).contains(NOW.plusSeconds(60));
    }

    @Test
    void 같은_대상을_다시_예약하면_시각만_바뀌고_하나만_남는다() {
        timer.schedulePhaseTimeout("g1", 3, NOW.plusSeconds(30));
        timer.schedulePhaseTimeout("g1", 3, NOW.plusSeconds(31));   // 잠금 실패 후 1초 뒤 재시도 같은 경우
        timer.schedulePhaseTimeout("g1", 3, NOW.plusSeconds(5));    // 더 이른 시각이어도 마지막 예약을 따른다

        assertThat(timer.size()).isEqualTo(1);
        assertThat(timer.scheduledAt(TimerKeys.phase("g1", 3))).contains(NOW.plusSeconds(5));
    }

    @Test
    void 버전이나_게임이_다르면_따로_저장한다() {
        timer.schedulePhaseTimeout("g1", 3, NOW.plusSeconds(30));
        timer.schedulePhaseTimeout("g1", 4, NOW.plusSeconds(30));
        timer.schedulePhaseTimeout("g2", 3, NOW.plusSeconds(30));
        timer.scheduleCleanup("g1", NOW.plusSeconds(30));

        assertThat(timer.size()).isEqualTo(4);
    }

    @Test
    void 밀리초까지_그대로_저장된다() {
        Instant at = NOW.plusMillis(1234);

        timer.schedulePhaseTimeout("g1", 1, at);

        assertThat(timer.scheduledAt(TimerKeys.phase("g1", 1))).contains(at);
    }

    @Test
    void 서버가_바뀌어도_예약이_남는다() {
        timer.schedulePhaseTimeout("g1", 3, NOW.plusSeconds(30));

        RedisGameTimer otherServer = new RedisGameTimer(gameRedis);   // 재시작했거나 다른 서버

        assertThat(otherServer.scheduledAt(TimerKeys.phase("g1", 3))).contains(NOW.plusSeconds(30));
    }

    @Test
    void 예약이_없으면_비어_있다() {
        assertThat(timer.scheduledAt(TimerKeys.phase("g1", 3))).isEmpty();
        assertThat(timer.size()).isZero();
    }
}
