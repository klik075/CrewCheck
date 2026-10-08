package com.WhoisntCitizen_server.lobby.lock;

import org.junit.jupiter.api.Test;

import java.util.concurrent.CompletableFuture;
import java.util.concurrent.CountDownLatch;
import java.util.concurrent.TimeUnit;
import java.util.concurrent.atomic.AtomicBoolean;

import static org.assertj.core.api.Assertions.assertThat;
import static org.assertj.core.api.Assertions.assertThatThrownBy;

class LocalRoomLockTest {

    private final LocalRoomLock roomLock = new LocalRoomLock();

    @Test
    void 결과를_돌려준다() {
        assertThat(roomLock.withLock(1L, () -> 42)).isEqualTo(42);
    }

    @Test
    void 같은_방을_잠근_채로_다시_잠가도_막히지_않는다() {
        String result = roomLock.withLock(1L, () -> roomLock.withLock(1L, () -> "inner"));

        assertThat(result).isEqualTo("inner");
    }

    @Test
    void 같은_방은_앞의_작업이_끝날_때까지_기다린다() throws Exception {
        CountDownLatch holding = new CountDownLatch(1);
        CountDownLatch release = new CountDownLatch(1);
        AtomicBoolean secondEntered = new AtomicBoolean(false);

        CompletableFuture<Void> first = CompletableFuture.runAsync(() -> roomLock.runWithLock(1L, () -> {
            holding.countDown();
            await(release);
        }));
        assertThat(holding.await(1, TimeUnit.SECONDS)).isTrue();

        CompletableFuture<Void> second = CompletableFuture.runAsync(() ->
                roomLock.runWithLock(1L, () -> secondEntered.set(true)));
        Thread.sleep(100);
        assertThat(secondEntered).isFalse();   // 첫 작업이 잠금을 쥐고 있는 동안 들어오지 못함

        release.countDown();
        first.get(1, TimeUnit.SECONDS);
        second.get(1, TimeUnit.SECONDS);
        assertThat(secondEntered).isTrue();
    }

    @Test
    void 다른_방끼리는_서로_기다리지_않는다() throws Exception {
        CountDownLatch holding = new CountDownLatch(1);
        CountDownLatch release = new CountDownLatch(1);
        CompletableFuture<Void> first = CompletableFuture.runAsync(() -> roomLock.runWithLock(1L, () -> {
            holding.countDown();
            await(release);
        }));
        assertThat(holding.await(1, TimeUnit.SECONDS)).isTrue();

        // 1번 방이 잠겨 있어도 2번 방은 바로 실행된다
        String other = CompletableFuture.supplyAsync(() -> roomLock.withLock(2L, () -> "room 2 done"))
                .get(1, TimeUnit.SECONDS);
        assertThat(other).isEqualTo("room 2 done");

        release.countDown();
        first.get(1, TimeUnit.SECONDS);
    }

    @Test
    void 작업에서_예외가_나도_잠금은_풀린다() {
        assertThatThrownBy(() -> roomLock.runWithLock(1L, () -> {
            throw new IllegalStateException("fail");
        })).isInstanceOf(IllegalStateException.class);

        String result = CompletableFuture.supplyAsync(() -> roomLock.withLock(1L, () -> "ok")).join();
        assertThat(result).isEqualTo("ok");
    }

    @Test
    void 방_번호가_없으면_거부한다() {
        assertThatThrownBy(() -> roomLock.withLock(null, () -> "x"))
                .isInstanceOf(NullPointerException.class);
    }

    private static void await(CountDownLatch latch) {
        try {
            latch.await(2, TimeUnit.SECONDS);
        } catch (InterruptedException e) {
            Thread.currentThread().interrupt();
        }
    }
}
