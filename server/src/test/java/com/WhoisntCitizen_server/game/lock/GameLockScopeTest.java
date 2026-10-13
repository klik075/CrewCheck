package com.WhoisntCitizen_server.game.lock;

import com.WhoisntCitizen_server.common.exception.LockTimeoutException;
import org.junit.jupiter.api.AfterEach;
import org.junit.jupiter.api.Test;

import java.time.Duration;
import java.util.ArrayList;
import java.util.List;
import java.util.concurrent.CompletableFuture;
import java.util.concurrent.CountDownLatch;
import java.util.concurrent.TimeUnit;

import static org.assertj.core.api.Assertions.assertThat;
import static org.assertj.core.api.Assertions.assertThatThrownBy;

/**
 * 2-8: 게임 잠금이 풀린 뒤 할 일(GameLockScope.afterUnlock)이 정해진 때·순서로 실행되는지 확인한다.
 * 잠금 구현은 LocalGameLock을 쓴다. (RedisGameLock도 같은 enter/exit를 부른다)
 */
class GameLockScopeTest {

    private final LocalGameLock lock = new LocalGameLock(Duration.ofMillis(300));
    private final List<String> log = new ArrayList<>();

    @AfterEach
    void noScopeLeft() {
        assertThat(GameLockScope.isHeld()).as("테스트가 끝나면 잠금 범위가 남아 있으면 안 된다").isFalse();
    }

    @Test
    void 잠금_밖에서는_바로_실행한다() {
        GameLockScope.afterUnlock(() -> log.add("바로"));

        assertThat(log).containsExactly("바로");
    }

    @Test
    void 잠금_안에서는_잠금이_풀린_뒤_넣은_순서대로_실행한다() {
        lock.runWithLock("g1", () -> {
            GameLockScope.afterUnlock(() -> log.add("안내1"));
            GameLockScope.afterUnlock(() -> log.add("안내2"));
            log.add("상태 변경");
            assertThat(GameLockScope.isHeld()).isTrue();
        });

        assertThat(log).containsExactly("상태 변경", "안내1", "안내2");
    }

    @Test
    void 재진입하면_가장_바깥_잠금이_풀릴_때까지_기다린다() {
        lock.runWithLock("g1", () -> {
            lock.runWithLock("g1", () -> GameLockScope.afterUnlock(() -> log.add("안쪽에서 넣음")));
            log.add("안쪽 잠금 풀림");
        });

        assertThat(log).containsExactly("안쪽 잠금 풀림", "안쪽에서 넣음");
    }

    @Test
    void 실행될_때는_잠금이_정말_풀려_있어_다른_스레드가_바로_잡을_수_있다() {
        lock.runWithLock("g1", () -> GameLockScope.afterUnlock(() -> {
            assertThat(GameLockScope.isHeld()).isFalse();
            // 다른 스레드가 짧은 대기 시간(300ms) 안에 같은 게임 잠금을 잡을 수 있어야 한다
            String other = CompletableFuture.supplyAsync(() -> lock.<String>withLock("g1", () -> "잡음")).join();
            log.add(other);
        }));

        assertThat(log).containsExactly("잡음");
    }

    @Test
    void 작업이_예외로_끝나도_모아_둔_일은_실행하고_예외는_그대로_전달한다() {
        assertThatThrownBy(() -> lock.runWithLock("g1", () -> {
            GameLockScope.afterUnlock(() -> log.add("안내"));
            throw new IllegalStateException("규칙 위반");
        })).isInstanceOf(IllegalStateException.class).hasMessage("규칙 위반");

        assertThat(log).containsExactly("안내");
    }

    @Test
    void 모아_둔_일_하나가_실패해도_나머지는_실행한다() {
        lock.runWithLock("g1", () -> {
            GameLockScope.afterUnlock(() -> {
                throw new IllegalStateException("채팅 저장 실패");
            });
            GameLockScope.afterUnlock(() -> log.add("다음 안내"));
        });

        assertThat(log).containsExactly("다음 안내");
    }

    @Test
    void 모아_둔_일이_다시_게임_잠금을_잡으면_새_범위로_실행된다() {
        lock.runWithLock("g1", () -> GameLockScope.afterUnlock(() ->
                lock.runWithLock("g1", () -> {
                    GameLockScope.afterUnlock(() -> log.add("두 번째 범위"));
                    log.add("다시 잠금");
                })));

        assertThat(log).containsExactly("다시 잠금", "두 번째 범위");
    }

    @Test
    void 잠금을_못_잡으면_범위가_남지_않는다() throws Exception {
        CountDownLatch held = new CountDownLatch(1);
        CountDownLatch release = new CountDownLatch(1);
        CompletableFuture<Void> holder = CompletableFuture.runAsync(() -> lock.runWithLock("g1", () -> {
            held.countDown();
            await(release);
        }));
        assertThat(held.await(5, TimeUnit.SECONDS)).isTrue();

        assertThatThrownBy(() -> lock.runWithLock("g1", () -> log.add("실행되면 안 됨")))
                .isInstanceOf(LockTimeoutException.class);
        assertThat(GameLockScope.isHeld()).isFalse();
        GameLockScope.afterUnlock(() -> log.add("바로"));   // 범위가 없으니 바로 실행

        release.countDown();
        holder.get(5, TimeUnit.SECONDS);
        assertThat(log).containsExactly("바로");
    }

    @Test
    void 다른_스레드의_잠금은_이_스레드에_보이지_않는다() throws Exception {
        CountDownLatch held = new CountDownLatch(1);
        CountDownLatch release = new CountDownLatch(1);
        CompletableFuture<Void> holder = CompletableFuture.runAsync(() -> lock.runWithLock("g2", () -> {
            held.countDown();
            await(release);
        }));
        assertThat(held.await(5, TimeUnit.SECONDS)).isTrue();

        assertThat(GameLockScope.isHeld()).isFalse();

        release.countDown();
        holder.get(5, TimeUnit.SECONDS);
    }

    private static void await(CountDownLatch latch) {
        try {
            latch.await(5, TimeUnit.SECONDS);
        } catch (InterruptedException e) {
            Thread.currentThread().interrupt();
        }
    }
}
