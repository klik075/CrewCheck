package com.WhoisntCitizen_server.support;

import com.WhoisntCitizen_server.common.exception.LockTimeoutException;
import org.junit.jupiter.api.Test;

import java.time.Duration;
import java.util.ArrayList;
import java.util.List;
import java.util.concurrent.CompletableFuture;
import java.util.concurrent.CountDownLatch;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;
import java.util.concurrent.Future;
import java.util.concurrent.TimeUnit;
import java.util.concurrent.atomic.AtomicBoolean;
import java.util.concurrent.atomic.AtomicInteger;
import java.util.function.Supplier;

import static org.assertj.core.api.Assertions.assertThat;
import static org.assertj.core.api.Assertions.assertThatThrownBy;

/**
 * 잠금 구현이라면 모두 지켜야 하는 동작 (게임 잠금 GameLock, 방 잠금 RoomLock 공통).
 * 구현마다 이 클래스를 상속해 잠금을 만드는 방법과 키 두 개만 채운다.
 *  - LocalGameLockTest, LocalRoomLockTest (지금, 서버 메모리)
 *  - RedisGameLock, RedisRoomLock도 2-6 통합 테스트에서 상속해 같은 동작을 실제 Redis에서 확인한다.
 *
 * 잠금이 지켜야 할 것
 *  1. 결과를 돌려준다
 *  2. 재진입: 쥔 채로 같은 키를 다시 잠가도 막히지 않는다 (GameService → GameFlowService처럼 겹쳐 부르는 곳이 있다)
 *  3. 같은 키는 한 번에 하나만 실행된다
 *  4. 다른 키끼리는 서로 기다리지 않는다
 *  5. 작업에서 예외가 나도 잠금이 풀리고, 예외는 그대로 전달된다
 *  6. 대기 시간을 넘기면 LockTimeoutException을 던지고 작업은 실행하지 않는다
 *  7. 키가 null이면 거부한다
 *
 * @param <L> 잠금 타입 (GameLock, RoomLock)
 * @param <K> 키 타입 (gameId는 String, roomId는 Long)
 */
public abstract class LockContractTest<L, K> {

    /** 기다리는 쪽이 들어오면 안 되는지 확인할 때 쓰는 짧은 대기 시간 */
    protected static final Duration SHORT_WAIT = Duration.ofMillis(300);
    /** 기다리면 결국 들어와야 하는 테스트에 쓰는 넉넉한 대기 시간 */
    protected static final Duration LONG_WAIT = Duration.ofSeconds(5);

    /** 대기 시간이 waitTimeout인 잠금을 새로 만든다. */
    protected abstract L newLock(Duration waitTimeout);

    /** lock으로 key를 잠그고 action을 실행한다. (GameLock.withLock / RoomLock.withLock) */
    protected abstract <T> T withLock(L lock, K key, Supplier<T> action);

    protected abstract K key1();

    protected abstract K key2();

    private void runWithLock(L lock, K key, Runnable action) {
        withLock(lock, key, () -> {
            action.run();
            return null;
        });
    }

    // ---------- 1~2. 결과, 재진입 ----------

    @Test
    void 결과를_돌려준다() {
        L lock = newLock(LONG_WAIT);

        assertThat(withLock(lock, key1(), () -> 42)).isEqualTo(42);
    }

    @Test
    void 쥔_채로_같은_키를_다시_잠가도_막히지_않는다() {
        L lock = newLock(SHORT_WAIT);

        String result = withLock(lock, key1(), () -> withLock(lock, key1(), () -> "inner"));

        assertThat(result).isEqualTo("inner");
    }

    // ---------- 3. 같은 키는 하나씩 ----------

    @Test
    void 같은_키는_앞의_작업이_끝날_때까지_기다린다() throws Exception {
        L lock = newLock(LONG_WAIT);
        CountDownLatch holding = new CountDownLatch(1);
        CountDownLatch release = new CountDownLatch(1);
        AtomicBoolean secondEntered = new AtomicBoolean(false);

        CompletableFuture<Void> first = CompletableFuture.runAsync(() -> runWithLock(lock, key1(), () -> {
            holding.countDown();
            await(release);
        }));
        assertThat(holding.await(2, TimeUnit.SECONDS)).isTrue();

        CompletableFuture<Void> second = CompletableFuture.runAsync(() ->
                runWithLock(lock, key1(), () -> secondEntered.set(true)));
        Thread.sleep(200);
        assertThat(secondEntered).isFalse();   // 첫 작업이 잠금을 쥐고 있는 동안 들어오지 못함

        release.countDown();
        first.get(3, TimeUnit.SECONDS);
        second.get(3, TimeUnit.SECONDS);
        assertThat(secondEntered).isTrue();    // 첫 작업이 끝나면 들어옴
    }

    @Test
    void 여러_스레드가_같은_키로_동시에_실행해도_한_번에_하나씩만_들어간다() throws Exception {
        L lock = newLock(LONG_WAIT);
        int threads = 8;
        int rounds = 50;
        AtomicInteger inside = new AtomicInteger();
        AtomicInteger maxInside = new AtomicInteger();
        int[] counter = {0}; // 일부러 동기화하지 않은 값. 잠금이 제대로면 정확히 threads * rounds가 된다

        ExecutorService pool = Executors.newFixedThreadPool(threads);
        try {
            List<Future<?>> futures = new ArrayList<>();
            for (int t = 0; t < threads; t++) {
                futures.add(pool.submit(() -> {
                    for (int r = 0; r < rounds; r++) {
                        runWithLock(lock, key1(), () -> {
                            maxInside.accumulateAndGet(inside.incrementAndGet(), Math::max);
                            counter[0] = counter[0] + 1;
                            inside.decrementAndGet();
                        });
                    }
                }));
            }
            for (Future<?> f : futures) {
                f.get(30, TimeUnit.SECONDS);
            }
        } finally {
            pool.shutdownNow();
        }

        assertThat(maxInside.get()).isEqualTo(1);
        assertThat(counter[0]).isEqualTo(threads * rounds);
    }

    // ---------- 4. 다른 키는 서로 안 기다림 ----------

    @Test
    void 다른_키끼리는_서로_기다리지_않는다() throws Exception {
        L lock = newLock(SHORT_WAIT);
        CountDownLatch holding = new CountDownLatch(1);
        CountDownLatch release = new CountDownLatch(1);
        CompletableFuture<Void> first = CompletableFuture.runAsync(() -> runWithLock(lock, key1(), () -> {
            holding.countDown();
            await(release);
        }));
        assertThat(holding.await(2, TimeUnit.SECONDS)).isTrue();

        // key1이 잠겨 있어도 key2는 대기 시간(짧음) 안에 바로 실행된다
        String other = CompletableFuture.supplyAsync(() -> withLock(lock, key2(), () -> "key2 done"))
                .get(2, TimeUnit.SECONDS);
        assertThat(other).isEqualTo("key2 done");

        release.countDown();
        first.get(3, TimeUnit.SECONDS);
    }

    // ---------- 5. 예외 ----------

    @Test
    void 작업에서_예외가_나면_그대로_전달되고_잠금은_풀린다() throws Exception {
        L lock = newLock(SHORT_WAIT);

        assertThatThrownBy(() -> runWithLock(lock, key1(), () -> {
            throw new IllegalStateException("fail");
        })).isInstanceOf(IllegalStateException.class).hasMessage("fail");

        // 다른 스레드가 바로 잡을 수 있다 (풀리지 않았다면 대기 시간 초과로 실패)
        String result = CompletableFuture.supplyAsync(() -> withLock(lock, key1(), () -> "ok"))
                .get(2, TimeUnit.SECONDS);
        assertThat(result).isEqualTo("ok");
    }

    @Test
    void 재진입한_안쪽에서_예외가_나도_바깥_잠금은_유지된다() throws Exception {
        L lock = newLock(SHORT_WAIT);
        CountDownLatch innerFailed = new CountDownLatch(1);
        CountDownLatch release = new CountDownLatch(1);

        CompletableFuture<Void> outer = CompletableFuture.runAsync(() -> runWithLock(lock, key1(), () -> {
            try {
                runWithLock(lock, key1(), () -> {
                    throw new IllegalStateException("inner fail");
                });
            } catch (IllegalStateException expected) {
                innerFailed.countDown();
            }
            await(release); // 안쪽만 풀렸고 바깥 잠금은 아직 쥐고 있어야 한다
        }));
        assertThat(innerFailed.await(2, TimeUnit.SECONDS)).isTrue();

        // 바깥이 아직 쥐고 있으므로 다른 스레드는 대기 시간 초과
        assertThatThrownBy(() -> CompletableFuture.supplyAsync(() -> withLock(lock, key1(), () -> "x"))
                .get(3, TimeUnit.SECONDS))
                .hasCauseInstanceOf(LockTimeoutException.class);

        release.countDown();
        outer.get(3, TimeUnit.SECONDS);
        // 바깥까지 끝나면 잡을 수 있다
        assertThat(CompletableFuture.supplyAsync(() -> withLock(lock, key1(), () -> "ok"))
                .get(2, TimeUnit.SECONDS)).isEqualTo("ok");
    }

    // ---------- 6. 대기 시간 초과 ----------

    @Test
    void 대기_시간을_넘기면_LockTimeoutException을_던지고_작업은_실행하지_않는다() throws Exception {
        L lock = newLock(SHORT_WAIT);
        CountDownLatch holding = new CountDownLatch(1);
        CountDownLatch release = new CountDownLatch(1);
        AtomicBoolean secondRan = new AtomicBoolean(false);
        CompletableFuture<Void> first = CompletableFuture.runAsync(() -> runWithLock(lock, key1(), () -> {
            holding.countDown();
            await(release);
        }));
        assertThat(holding.await(2, TimeUnit.SECONDS)).isTrue();

        long started = System.nanoTime();
        assertThatThrownBy(() -> CompletableFuture.runAsync(() ->
                        runWithLock(lock, key1(), () -> secondRan.set(true)))
                .get(3, TimeUnit.SECONDS))
                .hasCauseInstanceOf(LockTimeoutException.class);
        long waitedMs = TimeUnit.NANOSECONDS.toMillis(System.nanoTime() - started);

        assertThat(secondRan).isFalse();                                   // 잠금을 못 잡았으니 실행 안 됨
        assertThat(waitedMs).isGreaterThanOrEqualTo(SHORT_WAIT.toMillis() - 50); // 대기 시간만큼은 기다림

        release.countDown();
        first.get(3, TimeUnit.SECONDS);
    }

    @Test
    void 기다리다_포기한_쪽이_있어도_잠금을_쥔_쪽은_정상으로_끝나고_그다음엔_잡을_수_있다() throws Exception {
        L lock = newLock(SHORT_WAIT);
        CountDownLatch holding = new CountDownLatch(1);
        CountDownLatch release = new CountDownLatch(1);
        CompletableFuture<String> first = CompletableFuture.supplyAsync(() -> withLock(lock, key1(), () -> {
            holding.countDown();
            await(release);
            return "first done";
        }));
        assertThat(holding.await(2, TimeUnit.SECONDS)).isTrue();
        assertThatThrownBy(() -> CompletableFuture.supplyAsync(() -> withLock(lock, key1(), () -> "x"))
                .get(3, TimeUnit.SECONDS))
                .hasCauseInstanceOf(LockTimeoutException.class);

        release.countDown();

        assertThat(first.get(3, TimeUnit.SECONDS)).isEqualTo("first done");
        assertThat(CompletableFuture.supplyAsync(() -> withLock(lock, key1(), () -> "after"))
                .get(2, TimeUnit.SECONDS)).isEqualTo("after");
    }

    // ---------- 7. null 키 ----------

    @Test
    void 키가_null이면_거부한다() {
        L lock = newLock(SHORT_WAIT);

        assertThatThrownBy(() -> withLock(lock, null, () -> "x"))
                .isInstanceOf(NullPointerException.class);
    }

    protected static void await(CountDownLatch latch) {
        try {
            latch.await(5, TimeUnit.SECONDS);
        } catch (InterruptedException e) {
            Thread.currentThread().interrupt();
        }
    }
}
