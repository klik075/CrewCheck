package com.WhoisntCitizen_server.game.lock;

import org.junit.jupiter.api.Test;

import java.util.concurrent.CompletableFuture;
import java.util.concurrent.CountDownLatch;
import java.util.concurrent.TimeUnit;
import java.util.concurrent.atomic.AtomicBoolean;

import static org.assertj.core.api.Assertions.assertThat;

class LocalGameLockTest {

    private final LocalGameLock gameLock = new LocalGameLock();

    @Test
    void 결과를_돌려준다() {
        assertThat(gameLock.withLock("g1", () -> 42)).isEqualTo(42);
    }

    @Test
    void 같은_게임을_잠근_채로_다시_잠가도_막히지_않는다() {
        // NightService(잠금) → GameFlowService.resolveNight(다시 잠금) 경로와 같은 상황
        String result = gameLock.withLock("g1", () -> gameLock.withLock("g1", () -> "inner"));

        assertThat(result).isEqualTo("inner");
    }

    @Test
    void 같은_게임은_앞의_작업이_끝날_때까지_기다린다() throws Exception {
        CountDownLatch holding = new CountDownLatch(1);
        CountDownLatch release = new CountDownLatch(1);
        AtomicBoolean secondEntered = new AtomicBoolean(false);

        CompletableFuture<Void> first = CompletableFuture.runAsync(() -> gameLock.runWithLock("g1", () -> {
            holding.countDown();
            await(release);
        }));
        assertThat(holding.await(1, TimeUnit.SECONDS)).isTrue();

        CompletableFuture<Void> second = CompletableFuture.runAsync(() ->
                gameLock.runWithLock("g1", () -> secondEntered.set(true)));
        Thread.sleep(100);
        assertThat(secondEntered).isFalse();   // 첫 작업이 잠금을 쥐고 있는 동안 들어오지 못함

        release.countDown();
        first.get(1, TimeUnit.SECONDS);
        second.get(1, TimeUnit.SECONDS);
        assertThat(secondEntered).isTrue();
    }

    @Test
    void 다른_게임끼리는_서로_기다리지_않는다() throws Exception {
        CountDownLatch holding = new CountDownLatch(1);
        CountDownLatch release = new CountDownLatch(1);
        CompletableFuture<Void> first = CompletableFuture.runAsync(() -> gameLock.runWithLock("g1", () -> {
            holding.countDown();
            await(release);
        }));
        assertThat(holding.await(1, TimeUnit.SECONDS)).isTrue();

        // g1이 잠겨 있어도 g2는 바로 실행된다
        String other = CompletableFuture.supplyAsync(() -> gameLock.withLock("g2", () -> "g2 done"))
                .get(1, TimeUnit.SECONDS);
        assertThat(other).isEqualTo("g2 done");

        release.countDown();
        first.get(1, TimeUnit.SECONDS);
    }

    @Test
    void 작업에서_예외가_나도_잠금은_풀린다() {
        try {
            gameLock.runWithLock("g1", () -> {
                throw new IllegalStateException("fail");
            });
        } catch (IllegalStateException ignored) {
        }

        String result = CompletableFuture.supplyAsync(() -> gameLock.withLock("g1", () -> "ok")).join();
        assertThat(result).isEqualTo("ok");
    }

    private static void await(CountDownLatch latch) {
        try {
            latch.await(2, TimeUnit.SECONDS);
        } catch (InterruptedException e) {
            Thread.currentThread().interrupt();
        }
    }
}
