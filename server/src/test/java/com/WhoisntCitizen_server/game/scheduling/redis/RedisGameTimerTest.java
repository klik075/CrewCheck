package com.WhoisntCitizen_server.game.scheduling.redis;

import com.WhoisntCitizen_server.game.repository.redis.GameRedis;
import com.WhoisntCitizen_server.game.repository.redis.GameRedisProperties;
import com.WhoisntCitizen_server.game.scheduling.GameTimeoutHandler;
import com.WhoisntCitizen_server.game.scheduling.GameTimer;
import com.WhoisntCitizen_server.game.scheduling.TimerKeys;
import com.WhoisntCitizen_server.support.GameTimerContractTest;
import com.WhoisntCitizen_server.support.MutableClock;
import org.junit.jupiter.api.AfterAll;
import org.junit.jupiter.api.BeforeAll;
import org.junit.jupiter.api.Test;
import org.testcontainers.DockerClientFactory;
import org.testcontainers.containers.GenericContainer;
import org.testcontainers.utility.DockerImageName;

import java.time.Duration;
import java.time.Instant;
import java.util.ArrayList;
import java.util.Collections;
import java.util.List;
import java.util.concurrent.Callable;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;
import java.util.concurrent.Future;

import static org.assertj.core.api.Assertions.assertThat;
import static org.junit.jupiter.api.Assumptions.assumeTrue;

/**
 * RedisGameTimer를 실제 Redis로 확인한다. Docker가 꺼져 있으면 건너뛴다.
 *  - 타이머 계약(GameTimerContractTest): LocalGameTimer와 같은 9가지 동작을 Redis에서도 지키는지
 *  - 아래 테스트: Redis 구현에만 있는 것 (저장 형식, 가져가기·완료, 서버 여러 대, 실행 중 서버가 죽은 경우)
 * 시간은 MutableClock으로 움직이고, fireDue()는 pollOnce() 한 번이다.
 */
class RedisGameTimerTest extends GameTimerContractTest {

    private static GenericContainer<?> redisContainer;
    private static GameRedis gameRedis;

    private RedisGameTimer redisTimer;
    /** 이 테스트 클래스의 Redis 전용 테스트가 쓰는 기록용 handler */
    private final List<String> calls = Collections.synchronizedList(new ArrayList<>());

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

    /** 테스트마다 빈 Redis에서 시작한다. (부모의 @BeforeEach가 부른다) */
    @Override
    protected GameTimer newTimer(MutableClock clock, GameTimeoutHandler handler) {
        gameRedis.template().getConnectionFactory().getConnection().serverCommands().flushDb();
        redisTimer = new RedisGameTimer(gameRedis, () -> handler, clock);
        return redisTimer;
    }

    @Override
    protected void fireDue() {
        redisTimer.pollOnce();
    }

    /** calls에 기록하는 handler로 새 서버(타이머)를 만든다. 같은 Redis, 같은 시계를 쓴다. */
    private RedisGameTimer server() {
        return server(RedisGameTimer.DEFAULT_BATCH_SIZE);
    }

    private RedisGameTimer server(int batchSize) {
        GameTimeoutHandler recording = new GameTimeoutHandler() {
            @Override
            public void onPhaseTimeout(String gameId, long phaseVersion) {
                calls.add("phase " + gameId + " v" + phaseVersion);
            }

            @Override
            public void onCleanup(String gameId) {
                calls.add("cleanup " + gameId);
            }
        };
        return new RedisGameTimer(gameRedis, () -> recording, clock, RedisGameTimer.DEFAULT_LEASE, batchSize);
    }

    private Instant at(long seconds) {
        return NOW.plusSeconds(seconds);
    }

    // ---------- 저장 형식 ----------

    @Test
    void 페이즈_타이머는_대상_이름을_member로_시각을_점수로_저장한다() {
        redisTimer.schedulePhaseTimeout("g1", 3, at(30));

        Double score = gameRedis.template().opsForZSet().score("game:v1:timers", "phase:g1:3");
        assertThat(score).isEqualTo((double) at(30).toEpochMilli());
    }

    @Test
    void 같은_대상을_다시_예약하면_하나만_남고_시각만_바뀐다() {
        redisTimer.schedulePhaseTimeout("g1", 3, at(30));
        redisTimer.schedulePhaseTimeout("g1", 3, at(5));

        assertThat(redisTimer.size()).isEqualTo(1);
        assertThat(redisTimer.scheduledAt(TimerKeys.phase("g1", 3))).contains(at(5));
    }

    @Test
    void 밀리초까지_그대로_저장된다() {
        redisTimer.schedulePhaseTimeout("g1", 1, NOW.plusMillis(1234));

        assertThat(redisTimer.scheduledAt(TimerKeys.phase("g1", 1))).contains(NOW.plusMillis(1234));
    }

    // ---------- 가져가기 · 완료 ----------

    @Test
    void 실행이_끝나면_예약이_지워진다() {
        RedisGameTimer server = server();
        server.schedulePhaseTimeout("g1", 3, at(10));

        clock.advance(Duration.ofSeconds(10));
        assertThat(server.pollOnce()).isEqualTo(1);

        assertThat(server.size()).isZero();
    }

    @Test
    void 가져간_예약은_실행하는_동안_지금_더하기_lease로_밀려_있다() {
        List<Instant> seenWhileRunning = new ArrayList<>();
        RedisGameTimer[] holder = new RedisGameTimer[1];
        holder[0] = new RedisGameTimer(gameRedis, () -> new GameTimeoutHandler() {
            @Override
            public void onPhaseTimeout(String gameId, long phaseVersion) {
                holder[0].scheduledAt(TimerKeys.phase(gameId, phaseVersion)).ifPresent(seenWhileRunning::add);
            }

            @Override
            public void onCleanup(String gameId) {
            }
        }, clock);
        holder[0].schedulePhaseTimeout("g1", 3, at(10));

        clock.advance(Duration.ofSeconds(10));
        holder[0].pollOnce();

        assertThat(seenWhileRunning).containsExactly(at(10).plus(RedisGameTimer.DEFAULT_LEASE));
    }

    @Test
    void 아직_시간이_안_된_예약은_가져가지_않고_점수도_그대로다() {
        RedisGameTimer server = server();
        server.schedulePhaseTimeout("g1", 3, at(10));

        assertThat(server.pollOnce()).isZero();

        assertThat(server.scheduledAt(TimerKeys.phase("g1", 3))).contains(at(10));
    }

    @Test
    void 밀린_예약이_많으면_한_번에_batchSize개씩_실행한다() {
        RedisGameTimer server = server(100);
        for (int i = 0; i < 150; i++) {
            server.schedulePhaseTimeout("g" + i, 1, at(1));
        }
        clock.advance(Duration.ofSeconds(1));

        assertThat(server.pollOnce()).isEqualTo(100);
        assertThat(server.pollOnce()).isEqualTo(50);
        assertThat(server.pollOnce()).isZero();
        assertThat(calls).hasSize(150);
    }

    @Test
    void 알_수_없는_예약은_지우고_다른_예약은_실행한다() {
        RedisGameTimer server = server();
        gameRedis.template().opsForZSet().add("game:v1:timers", "something-else", at(1).toEpochMilli());
        server.schedulePhaseTimeout("g1", 3, at(1));
        clock.advance(Duration.ofSeconds(1));

        server.pollOnce();

        assertThat(calls).containsExactly("phase g1 v3");
        assertThat(server.size()).isZero();
    }

    // ---------- 지연 측정 ----------

    @Test
    void 가져갈_때_원래_예약_시각을_함께_가져온다() {
        RedisGameTimer server = server();
        server.schedulePhaseTimeout("g1", 1, NOW.plusMillis(10_250));
        server.scheduleCleanup("g2", at(12));
        clock.advance(Duration.ofSeconds(15));

        List<RedisGameTimer.Claimed> claimed = server.claimDue();

        assertThat(claimed).extracting(RedisGameTimer.Claimed::key).containsExactly("phase:g1:1", "cleanup:g2");
        assertThat(claimed).extracting(RedisGameTimer.Claimed::dueAt)
                .containsExactly(NOW.plusMillis(10_250).toEpochMilli(), at(12).toEpochMilli());
        assertThat(claimed).extracting(RedisGameTimer.Claimed::leaseUntil)
                .containsOnly(at(15).plus(RedisGameTimer.DEFAULT_LEASE).toEpochMilli());
    }

    @Test
    void 늦은_정도는_지금에서_예약_시각을_뺀_값이다() {
        RedisGameTimer.Claimed c = new RedisGameTimer.Claimed("phase:g1:1", 1_000, 31_000);

        assertThat(RedisGameTimer.lateMillis(c, 3_500)).isEqualTo(2_500);
        assertThat(RedisGameTimer.lateMillis(c, 1_000)).isZero();
        assertThat(RedisGameTimer.lateMillis(c, 500)).as("시계가 어긋나 음수면 0").isZero();
    }

    @Test
    void lease가_지나_다시_가져간_예약은_다시_실행할_수_있게_된_시각부터_잰다() {
        RedisGameTimer dying = server();
        dying.schedulePhaseTimeout("g1", 3, at(10));
        clock.advance(Duration.ofSeconds(10));
        dying.claimDue();                                      // 가져가고 죽음 → 점수가 at(10) + 30초

        clock.advance(RedisGameTimer.DEFAULT_LEASE);
        RedisGameTimer.Claimed again = server().claimDue().get(0);

        assertThat(again.dueAt()).isEqualTo(at(10).plus(RedisGameTimer.DEFAULT_LEASE).toEpochMilli());
    }

    // ---------- 서버 여러 대 ----------

    @Test
    void 서버가_바뀌어도_예약이_남아_다른_서버가_실행한다() {
        server().schedulePhaseTimeout("g1", 3, at(30));     // 예약한 서버는 이후 꺼졌다고 본다

        clock.advance(Duration.ofSeconds(30));
        server().pollOnce();                                // 재시작했거나 다른 서버

        assertThat(calls).containsExactly("phase g1 v3");
    }

    @Test
    void 여러_서버가_동시에_확인해도_예약마다_한_번만_실행한다() throws Exception {
        RedisGameTimer first = server();
        for (int i = 0; i < 200; i++) {
            first.schedulePhaseTimeout("g" + i, 1, at(1));
        }
        clock.advance(Duration.ofSeconds(1));

        List<RedisGameTimer> servers = List.of(server(10), server(10), server(10), server(10));
        ExecutorService pool = Executors.newFixedThreadPool(servers.size());
        try {
            List<Callable<Integer>> jobs = new ArrayList<>();
            for (RedisGameTimer s : servers) {
                jobs.add(() -> {
                    int fired = 0;
                    int n;
                    while ((n = s.pollOnce()) > 0) {
                        fired += n;
                    }
                    return fired;
                });
            }
            int total = 0;
            for (Future<Integer> f : pool.invokeAll(jobs)) {
                total += f.get();
            }

            assertThat(total).isEqualTo(200);
            assertThat(calls).hasSize(200).doesNotHaveDuplicates();
            assertThat(first.size()).isZero();
        } finally {
            pool.shutdownNow();
        }
    }

    @Test
    void 가져간_서버가_완료하지_못하고_죽으면_lease가_지난_뒤_다른_서버가_실행한다() {
        RedisGameTimer dying = server();
        dying.schedulePhaseTimeout("g1", 3, at(10));
        clock.advance(Duration.ofSeconds(10));
        assertThat(dying.claimDue()).hasSize(1);            // 가져가기만 하고 실행·완료 전에 죽음

        RedisGameTimer survivor = server();
        clock.advance(RedisGameTimer.DEFAULT_LEASE.minusSeconds(1));
        assertThat(survivor.pollOnce()).as("lease 동안은 다른 서버가 가져가지 못한다").isZero();
        clock.advance(Duration.ofSeconds(1));
        assertThat(survivor.pollOnce()).isEqualTo(1);

        assertThat(calls).containsExactly("phase g1 v3");
        assertThat(survivor.size()).isZero();
    }

    @Test
    void lease가_지나_다른_서버가_다시_가져갔으면_늦게_끝난_서버는_지우지_않는다() {
        RedisGameTimer slow = server();
        slow.schedulePhaseTimeout("g1", 3, at(10));
        clock.advance(Duration.ofSeconds(10));
        RedisGameTimer.Claimed slowClaim = slow.claimDue().get(0);

        clock.advance(RedisGameTimer.DEFAULT_LEASE);         // 실행이 lease보다 오래 걸림
        RedisGameTimer other = server();
        RedisGameTimer.Claimed otherClaim = other.claimDue().get(0);

        assertThat(slow.complete(slowClaim)).as("다른 서버가 가져간 예약은 지우지 않는다").isFalse();
        assertThat(other.scheduledAt(TimerKeys.phase("g1", 3))).isPresent();
        assertThat(other.complete(otherClaim)).isTrue();
        assertThat(other.size()).isZero();
    }
}
