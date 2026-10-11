package com.WhoisntCitizen_server.game.scheduling.redis;

import com.WhoisntCitizen_server.game.repository.redis.GameRedis;
import com.WhoisntCitizen_server.game.repository.redis.GameRedisProperties;
import com.WhoisntCitizen_server.game.scheduling.GameTimeoutHandler;
import com.WhoisntCitizen_server.game.scheduling.TimerKeys;
import com.WhoisntCitizen_server.support.MutableClock;
import org.junit.jupiter.api.AfterAll;
import org.junit.jupiter.api.AfterEach;
import org.junit.jupiter.api.BeforeAll;
import org.junit.jupiter.api.BeforeEach;
import org.junit.jupiter.api.Test;
import org.testcontainers.DockerClientFactory;
import org.testcontainers.containers.GenericContainer;
import org.testcontainers.utility.DockerImageName;

import java.time.Duration;
import java.time.Instant;
import java.util.List;
import java.util.Set;
import java.util.concurrent.ConcurrentHashMap;
import java.util.concurrent.CountDownLatch;
import java.util.concurrent.TimeUnit;
import java.util.function.BooleanSupplier;

import static org.assertj.core.api.Assertions.assertThat;
import static org.junit.jupiter.api.Assumptions.assumeTrue;

/**
 * 3-4 보완: 가져간 예약을 작업 스레드에 나눠 실행하는지 확인한다. Docker가 꺼져 있으면 건너뛴다.
 *  - 한 게임이 막혀도(게임 잠금 대기) 다른 게임은 다른 작업 스레드에서 실행된다
 *  - 빈 작업 스레드 수만큼만 가져가고, 나머지는 Redis에 그대로 둔다 (lease가 흐르지 않게)
 *
 * handler가 "막힘" 대상이면 release 래치가 열릴 때까지 기다린다. (다른 서버가 게임 잠금을 오래 쥔 상황)
 */
class RedisTimerDispatcherTest {

    private static final Instant NOW = Instant.parse("2026-10-11T12:00:00Z");
    private static final long WAIT_SECONDS = 5;

    private static GenericContainer<?> redisContainer;
    private static GameRedis gameRedis;

    private MutableClock clock;
    private RedisGameTimer timer;
    private RedisTimerDispatcher dispatcher;

    /** handler가 부른 대상 */
    private final Set<String> fired = ConcurrentHashMap.newKeySet();
    /** 이 게임들의 타이머는 release가 열릴 때까지 막힌다 */
    private final Set<String> blockedGames = ConcurrentHashMap.newKeySet();
    private final CountDownLatch release = new CountDownLatch(1);

    @BeforeAll
    static void startRedis() {
        assumeTrue(DockerClientFactory.instance().isDockerAvailable(),
                "Docker가 실행 중이 아니라 Redis 타이머 작업 스레드 테스트를 건너뜁니다");
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
        clock = new MutableClock(NOW);
        GameTimeoutHandler handler = new GameTimeoutHandler() {
            @Override
            public void onPhaseTimeout(String gameId, long phaseVersion) {
                if (blockedGames.contains(gameId)) {
                    await(release);
                }
                fired.add(gameId);
            }

            @Override
            public void onCleanup(String gameId) {
                fired.add("cleanup " + gameId);
            }
        };
        timer = new RedisGameTimer(gameRedis, () -> handler, clock);
    }

    @AfterEach
    void tearDown() {
        release.countDown();          // 막힌 작업 스레드를 풀어 준다
        if (dispatcher != null) {
            dispatcher.close();
        }
    }

    private RedisTimerDispatcher dispatcher(int workers) {
        dispatcher = new RedisTimerDispatcher(timer, workers);
        return dispatcher;
    }

    @Test
    void 한_게임이_막혀도_다른_게임은_다른_작업_스레드에서_실행된다() {
        RedisTimerDispatcher d = dispatcher(4);
        blockedGames.add("g1");
        timer.schedulePhaseTimeout("g1", 1, NOW.plusSeconds(1));
        timer.schedulePhaseTimeout("g2", 1, NOW.plusSeconds(1));
        timer.schedulePhaseTimeout("g3", 1, NOW.plusSeconds(1));
        clock.advance(Duration.ofSeconds(1));

        assertThat(d.dispatchOnce()).isEqualTo(3);

        assertThat(eventually(() -> fired.containsAll(List.of("g2", "g3"))))
                .as("g1이 막혀 있는 동안에도 g2, g3은 실행된다").isTrue();
        assertThat(fired).doesNotContain("g1");
        assertThat(eventually(() -> d.running() == 1)).as("g1만 아직 실행 중").isTrue();

        release.countDown();
        assertThat(eventually(() -> fired.contains("g1"))).isTrue();
    }

    @Test
    void 빈_작업_스레드_수만큼만_가져가고_나머지는_Redis에_그대로_둔다() {
        RedisTimerDispatcher d = dispatcher(2);
        for (int i = 1; i <= 5; i++) {
            blockedGames.add("g" + i);
            timer.schedulePhaseTimeout("g" + i, 1, NOW.plusSeconds(i));
        }
        clock.advance(Duration.ofSeconds(5));

        assertThat(d.dispatchOnce()).isEqualTo(2);             // g1, g2 (오래된 것부터)
        assertThat(d.dispatchOnce()).as("작업 스레드가 모두 바쁘면 더 가져가지 않는다").isZero();
        for (int i = 3; i <= 5; i++) {
            assertThat(timer.scheduledAt(TimerKeys.phase("g" + i, 1)))
                    .as("가져가지 않은 예약은 원래 시각 그대로 (lease가 흐르지 않음)")
                    .contains(NOW.plusSeconds(i));
        }

        release.countDown();                                   // g1, g2 실행이 끝나 작업 스레드가 빔
        assertThat(eventually(() -> d.running() == 0)).isTrue();
        assertThat(d.dispatchOnce()).isEqualTo(2);             // g3, g4
        assertThat(eventually(() -> d.running() == 0)).isTrue();
        assertThat(d.dispatchOnce()).isEqualTo(1);             // g5
        assertThat(eventually(() -> fired.size() == 5)).isTrue();
    }

    @Test
    void 실행이_끝나면_예약이_지워진다() {
        RedisTimerDispatcher d = dispatcher(4);
        timer.schedulePhaseTimeout("g1", 1, NOW.plusSeconds(1));
        timer.scheduleCleanup("g2", NOW.plusSeconds(1));
        clock.advance(Duration.ofSeconds(1));

        d.dispatchOnce();

        assertThat(eventually(() -> timer.size() == 0)).isTrue();
        assertThat(fired).containsExactlyInAnyOrder("g1", "cleanup g2");
    }

    @Test
    void 시간이_된_예약이_없으면_아무것도_넘기지_않는다() {
        RedisTimerDispatcher d = dispatcher(4);
        timer.schedulePhaseTimeout("g1", 1, NOW.plusSeconds(10));

        assertThat(d.dispatchOnce()).isZero();
        assertThat(d.running()).isZero();
    }

    @Test
    void 작업_스레드를_멈추면_실행_중인_타이머는_끝까지_마친다() {
        RedisTimerDispatcher d = dispatcher(2);
        timer.schedulePhaseTimeout("g1", 1, NOW.plusSeconds(1));
        clock.advance(Duration.ofSeconds(1));
        d.dispatchOnce();

        d.close();

        assertThat(fired).contains("g1");
        assertThat(timer.size()).isZero();
    }

    /** 조건이 WAIT_SECONDS 안에 참이 되면 true */
    private static boolean eventually(BooleanSupplier condition) {
        long deadline = System.nanoTime() + TimeUnit.SECONDS.toNanos(WAIT_SECONDS);
        while (System.nanoTime() < deadline) {
            if (condition.getAsBoolean()) {
                return true;
            }
            try {
                Thread.sleep(10);
            } catch (InterruptedException e) {
                Thread.currentThread().interrupt();
                return false;
            }
        }
        return condition.getAsBoolean();
    }

    private static void await(CountDownLatch latch) {
        try {
            latch.await(WAIT_SECONDS * 2, TimeUnit.SECONDS);
        } catch (InterruptedException e) {
            Thread.currentThread().interrupt();
        }
    }
}
