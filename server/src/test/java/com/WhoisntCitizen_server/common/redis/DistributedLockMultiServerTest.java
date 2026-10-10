package com.WhoisntCitizen_server.common.redis;

import com.WhoisntCitizen_server.common.exception.LockTimeoutException;
import com.WhoisntCitizen_server.game.lock.GameLock;
import com.WhoisntCitizen_server.game.lock.redis.RedisGameLock;
import com.WhoisntCitizen_server.lobby.lock.RoomLock;
import com.WhoisntCitizen_server.lobby.lock.redis.RedisRoomLock;
import org.junit.jupiter.api.AfterAll;
import org.junit.jupiter.api.AfterEach;
import org.junit.jupiter.api.BeforeAll;
import org.junit.jupiter.api.BeforeEach;
import org.junit.jupiter.api.Test;
import org.redisson.api.RBucket;
import org.redisson.api.RLock;
import org.redisson.api.RedissonClient;
import org.testcontainers.DockerClientFactory;
import org.testcontainers.containers.GenericContainer;
import org.testcontainers.utility.DockerImageName;

import java.time.Duration;
import java.util.ArrayList;
import java.util.List;
import java.util.UUID;
import java.util.concurrent.CompletableFuture;
import java.util.concurrent.CountDownLatch;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;
import java.util.concurrent.Future;
import java.util.concurrent.TimeUnit;

import static org.assertj.core.api.Assertions.assertThat;
import static org.assertj.core.api.Assertions.assertThatThrownBy;
import static org.junit.jupiter.api.Assumptions.assumeTrue;

/**
 * 2-6: 서버 2대가 같은 Redis 잠금을 두고 경쟁하는 상황을 확인한다.
 *
 * "서버 1대" = Redisson 연결(RedissonClient) 1개. 연결마다 고유 id가 있어서, Redisson은 잠금 주인을
 * "어느 연결(서버)의 어느 스레드"로 구분한다. 그래서 연결 2개를 만들면 실제 서버 2대와 같은 조건이 된다.
 *
 * 계약 테스트(RedisGameLockTest, RedisRoomLockTest)는 연결 1개 안의 스레드 경쟁을 보고,
 * 여기서는 연결끼리의 경쟁과 한 연결이 갑자기 사라졌을 때(서버 다운)를 본다. Docker가 없으면 건너뛴다.
 */
class DistributedLockMultiServerTest {

    /** 테스트에서 쓰는 잠금 자동 연장 시간. 30초를 기다리지 않도록 짧게 준다. (연장은 이 시간의 1/3마다) */
    private static final Duration WATCHDOG = Duration.ofSeconds(2);
    private static final Duration SHORT_WAIT = Duration.ofMillis(300);
    private static final Duration LONG_WAIT = Duration.ofSeconds(10);

    private static GenericContainer<?> redisContainer;

    private RedissonClient serverA;
    private RedissonClient serverB;
    private final String gameId = "multi-" + UUID.randomUUID();

    @BeforeAll
    static void startRedis() {
        assumeTrue(DockerClientFactory.instance().isDockerAvailable(),
                "Docker가 실행 중이 아니라 서버 2대 잠금 테스트를 건너뜁니다");
        redisContainer = new GenericContainer<>(DockerImageName.parse("redis:7-alpine")).withExposedPorts(6379);
        redisContainer.start();
    }

    @AfterAll
    static void stopRedis() {
        if (redisContainer != null) {
            redisContainer.stop();
        }
    }

    private static RedissonClient newServer() {
        return RedissonClients.create(redisContainer.getHost(), redisContainer.getMappedPort(6379), 0, "", WATCHDOG);
    }

    @BeforeEach
    void startServers() {
        serverA = newServer();
        serverB = newServer();
    }

    @AfterEach
    void stopServers() {
        if (!serverA.isShutdown()) {
            serverA.shutdown();
        }
        if (!serverB.isShutdown()) {
            serverB.shutdown();
        }
    }

    // ---------- 두 서버의 경쟁 ----------

    @Test
    void 두_서버가_같은_게임을_동시에_고쳐도_변경이_사라지지_않는다() throws Exception {
        // 게임 상태 저장(읽기 → 고치기 → 저장)을 흉내 낸다. 잠금이 서버 사이에서 지켜지지 않으면 덮어쓰기로 숫자가 사라진다
        GameLock lockA = new RedisGameLock(serverA, LONG_WAIT);
        GameLock lockB = new RedisGameLock(serverB, LONG_WAIT);
        RBucket<Integer> stateA = serverA.getBucket("multi-state:" + gameId);
        RBucket<Integer> stateB = serverB.getBucket("multi-state:" + gameId);
        stateA.set(0);
        int threadsPerServer = 4;
        int rounds = 25;

        ExecutorService pool = Executors.newFixedThreadPool(threadsPerServer * 2);
        try {
            List<Future<?>> futures = new ArrayList<>();
            for (int t = 0; t < threadsPerServer; t++) {
                futures.add(pool.submit(() -> increment(lockA, stateA, rounds)));
                futures.add(pool.submit(() -> increment(lockB, stateB, rounds)));
            }
            for (Future<?> f : futures) {
                f.get(60, TimeUnit.SECONDS);
            }
        } finally {
            pool.shutdownNow();
        }

        assertThat(stateA.get()).isEqualTo(threadsPerServer * 2 * rounds);
    }

    private void increment(GameLock lock, RBucket<Integer> state, int rounds) {
        for (int r = 0; r < rounds; r++) {
            lock.runWithLock(gameId, () -> {
                int current = state.get();   // 읽기
                state.set(current + 1);      // 고쳐서 저장
            });
        }
    }

    @Test
    void A_서버가_쥐고_있으면_B_서버는_대기_시간_초과이고_A가_풀면_잡는다() throws Exception {
        GameLock lockA = new RedisGameLock(serverA, LONG_WAIT);
        GameLock lockB = new RedisGameLock(serverB, SHORT_WAIT);
        CountDownLatch holding = new CountDownLatch(1);
        CountDownLatch release = new CountDownLatch(1);

        CompletableFuture<Void> a = CompletableFuture.runAsync(() -> lockA.runWithLock(gameId, () -> {
            holding.countDown();
            await(release);
        }));
        assertThat(holding.await(5, TimeUnit.SECONDS)).isTrue();

        assertThatThrownBy(() -> lockB.withLock(gameId, () -> "B"))
                .isInstanceOf(LockTimeoutException.class);

        release.countDown();
        a.get(5, TimeUnit.SECONDS);
        assertThat(lockB.withLock(gameId, () -> "B")).isEqualTo("B");
    }

    @Test
    void 같은_스레드라도_다른_서버의_잠금은_재진입으로_보지_않는다() {
        // 재진입은 "같은 서버의 같은 스레드"일 때만이다. 서버가 다르면 남의 잠금이다
        GameLock lockA = new RedisGameLock(serverA, LONG_WAIT);
        GameLock lockB = new RedisGameLock(serverB, SHORT_WAIT);

        lockA.runWithLock(gameId, () ->
                assertThatThrownBy(() -> lockB.withLock(gameId, () -> "B"))
                        .isInstanceOf(LockTimeoutException.class));
    }

    @Test
    void 방_잠금도_서버_사이에서_지켜진다() throws Exception {
        long roomId = Math.abs(UUID.randomUUID().getMostSignificantBits() % 1_000_000_000L) + 1;
        RoomLock lockA = new RedisRoomLock(serverA, LONG_WAIT);
        RoomLock lockB = new RedisRoomLock(serverB, SHORT_WAIT);
        CountDownLatch holding = new CountDownLatch(1);
        CountDownLatch release = new CountDownLatch(1);

        CompletableFuture<Void> a = CompletableFuture.runAsync(() -> lockA.runWithLock(roomId, () -> {
            holding.countDown();
            await(release);
        }));
        assertThat(holding.await(5, TimeUnit.SECONDS)).isTrue();

        assertThatThrownBy(() -> lockB.withLock(roomId, () -> "B"))
                .isInstanceOf(LockTimeoutException.class);

        release.countDown();
        a.get(5, TimeUnit.SECONDS);
        assertThat(lockB.withLock(roomId, () -> "B")).isEqualTo("B");
    }

    // ---------- 자동 연장과 서버 다운 ----------

    @Test
    void 살아_있는_서버는_자동_연장_시간보다_오래_쥐어도_잠금을_잃지_않는다() throws Exception {
        // 자동 연장(2초)보다 오래(5초) 쥐고 있어도 그동안 B는 잡지 못한다 → 연장이 동작한다
        GameLock lockA = new RedisGameLock(serverA, LONG_WAIT);
        GameLock lockB = new RedisGameLock(serverB, SHORT_WAIT);
        CountDownLatch holding = new CountDownLatch(1);
        CountDownLatch release = new CountDownLatch(1);

        CompletableFuture<Void> a = CompletableFuture.runAsync(() -> lockA.runWithLock(gameId, () -> {
            holding.countDown();
            await(release);
        }));
        assertThat(holding.await(5, TimeUnit.SECONDS)).isTrue();

        Thread.sleep(WATCHDOG.toMillis() * 2 + 1000);   // 연장 없이 만료됐을 시간을 충분히 넘김
        assertThatThrownBy(() -> lockB.withLock(gameId, () -> "B"))
                .isInstanceOf(LockTimeoutException.class);

        release.countDown();
        a.get(5, TimeUnit.SECONDS);
    }

    @Test
    void 잠금을_쥔_서버가_죽으면_자동_연장이_멈춰_일정_시간_뒤_다른_서버가_잡는다() {
        // A 서버가 잠금을 쥔 채 풀지 않고 죽는다 (unlock 없이 연결 종료)
        RLock heldByA = serverA.getLock("game:lock:" + gameId);
        heldByA.lock();                                   // 쥐는 시간 없이 잡음 → 자동 연장
        serverA.shutdown();                               // 서버 다운: 연장도, 해제도 일어나지 않음
        long downAt = System.nanoTime();

        // 죽자마자 풀리지는 않는다 (Redis에 만료 시간이 남아 있음)
        GameLock shortWaitB = new RedisGameLock(serverB, SHORT_WAIT);
        assertThatThrownBy(() -> shortWaitB.withLock(gameId, () -> "B"))
                .isInstanceOf(LockTimeoutException.class);

        // 만료 시간(최대 자동 연장 시간)이 지나면 B가 잡는다
        GameLock longWaitB = new RedisGameLock(serverB, LONG_WAIT);
        String result = longWaitB.withLock(gameId, () -> "B took over");
        long tookOverMs = TimeUnit.NANOSECONDS.toMillis(System.nanoTime() - downAt);

        assertThat(result).isEqualTo("B took over");
        assertThat(tookOverMs).isLessThanOrEqualTo(WATCHDOG.toMillis() + 2000); // 자동 연장 시간 + 여유
    }

    private static void await(CountDownLatch latch) {
        try {
            latch.await(15, TimeUnit.SECONDS);
        } catch (InterruptedException e) {
            Thread.currentThread().interrupt();
        }
    }
}
