package com.WhoisntCitizen_server.support;

import com.WhoisntCitizen_server.game.scheduling.GameTimeoutHandler;
import com.WhoisntCitizen_server.game.scheduling.GameTimer;
import org.junit.jupiter.api.BeforeEach;
import org.junit.jupiter.api.Test;

import java.time.Duration;
import java.time.Instant;
import java.util.ArrayList;
import java.util.List;
import java.util.function.Consumer;

import static org.assertj.core.api.Assertions.assertThat;
import static org.assertj.core.api.Assertions.assertThatCode;

/**
 * 게임 타이머 구현이라면 모두 지켜야 하는 동작 (GameTimer 계약).
 * 구현마다 이 클래스를 상속해 타이머를 만드는 방법과 "시간이 된 예약을 실행하는 방법" 두 가지만 채운다.
 *  - LocalGameTimerTest (서버 메모리 스케줄러)
 *  - RedisGameTimerTest (3-3 이후, Redis ZSET)
 *
 * 시간은 MutableClock으로 직접 움직인다. clock.advance(...) 뒤 fireDue()를 부르면 그 시각까지 된 예약이 실행된다.
 *
 * 타이머가 지켜야 할 것
 *  1. 예약 시각 전에는 실행하지 않는다
 *  2. 예약 시각이 되면 실행하고, 페이즈 타이머는 예약할 때 준 버전을 그대로 넘긴다
 *  3. 정리 타이머도 같은 방식으로 실행한다
 *  4. 이미 지난 시각으로 예약하면 다음 확인 때 바로 실행한다 (서버 재시작 복구, 잠금 실패 후 재시도)
 *  5. 시각이 된 예약이 여러 개면 모두, 시각 순서대로 실행한다
 *  6. 같은 대상(같은 게임의 같은 버전 / 같은 게임의 정리)을 다시 예약하면 마지막 예약 하나만 남는다
 *     예전 시각에는 실행하지 않고 새 시각에 한 번만 실행한다
 *  7. 버전이 다르거나 게임이 다르면 서로 다른 예약이다
 *  8. 실행 중에 같은 대상을 다시 예약하면(잠금을 못 잡아 1초 뒤 재시도) 그 새 예약은 지워지지 않는다
 *  9. 한 예약의 실행이 예외로 끝나도 밖으로 던지지 않고, 다른 예약은 계속 실행한다
 *
 * 받는 쪽(GameFlowService)은 버전으로 지난 페이즈를 무시하므로, 같은 예약이 드물게 두 번 실행되는 것은 허용한다.
 * (서버가 실행 도중 죽은 경우 등. Redis 구현에서 다룬다) 위 6번은 "평소에는 한 번"을 보장하는 규칙이다.
 */
public abstract class GameTimerContractTest {

    protected static final Instant NOW = Instant.parse("2026-10-10T12:00:00Z");

    protected MutableClock clock;
    private RecordingHandler handler;
    protected GameTimer timer;

    /** clock을 기준으로 시간을 재고, 시간이 되면 handler를 부르는 타이머를 만든다. */
    protected abstract GameTimer newTimer(MutableClock clock, GameTimeoutHandler handler);

    /** 지금(clock) 시각까지 된 예약을 실행한다. (서버 메모리: 스케줄러 실행 / Redis: 한 번 확인) */
    protected abstract void fireDue();

    @BeforeEach
    void setUpTimer() {
        clock = new MutableClock(NOW);
        handler = new RecordingHandler();
        timer = newTimer(clock, handler);
    }

    /** 시계를 seconds만큼 옮기고 그 시각까지 된 예약을 실행한다. */
    private void advance(long seconds) {
        clock.advance(Duration.ofSeconds(seconds));
        fireDue();
    }

    private Instant at(long seconds) {
        return NOW.plusSeconds(seconds);
    }

    // ---------- 1~3. 기본 ----------

    @Test
    void 예약_시각_전에는_실행하지_않는다() {
        timer.schedulePhaseTimeout("g1", 3, at(30));

        advance(29);

        assertThat(handler.calls).isEmpty();
    }

    @Test
    void 예약_시각이_되면_예약할_때_준_버전으로_페이즈_종료를_알린다() {
        timer.schedulePhaseTimeout("g1", 3, at(30));

        advance(30);

        assertThat(handler.calls).containsExactly("phase g1 v3");
    }

    @Test
    void 정리_타이머도_시각이_되면_실행한다() {
        timer.scheduleCleanup("g1", at(60));

        advance(59);
        assertThat(handler.calls).isEmpty();
        advance(1);

        assertThat(handler.calls).containsExactly("cleanup g1");
    }

    @Test
    void 한_번_실행한_예약은_다시_실행하지_않는다() {
        timer.schedulePhaseTimeout("g1", 3, at(30));

        advance(30);
        advance(60);

        assertThat(handler.calls).containsExactly("phase g1 v3");
    }

    // ---------- 4~5. 지난 시각, 여러 예약 ----------

    @Test
    void 이미_지난_시각으로_예약하면_다음_확인_때_바로_실행한다() {
        timer.schedulePhaseTimeout("g1", 3, NOW.minusSeconds(10));

        fireDue();

        assertThat(handler.calls).containsExactly("phase g1 v3");
    }

    @Test
    void 시각이_된_예약이_여러_개면_모두_시각_순서대로_실행한다() {
        timer.scheduleCleanup("g1", at(60));
        timer.schedulePhaseTimeout("g2", 1, at(5));
        timer.schedulePhaseTimeout("g3", 7, at(30));

        advance(60);

        assertThat(handler.calls).containsExactly("phase g2 v1", "phase g3 v7", "cleanup g1");
    }

    // ---------- 6~7. 다시 예약 ----------

    @Test
    void 같은_페이즈를_다시_예약하면_새_시각에_한_번만_실행한다() {
        timer.schedulePhaseTimeout("g1", 3, at(10));
        timer.schedulePhaseTimeout("g1", 3, at(20));

        advance(10);
        assertThat(handler.calls).as("예전 시각에는 실행하지 않는다").isEmpty();
        advance(10);
        advance(30);

        assertThat(handler.calls).containsExactly("phase g1 v3");
    }

    @Test
    void 더_이른_시각으로_다시_예약해도_새_시각을_따른다() {
        timer.scheduleCleanup("g1", at(60));
        timer.scheduleCleanup("g1", at(5));

        advance(5);
        assertThat(handler.calls).containsExactly("cleanup g1");
        advance(60);

        assertThat(handler.calls).containsExactly("cleanup g1");
    }

    @Test
    void 버전이나_게임이_다르면_서로_다른_예약이다() {
        timer.schedulePhaseTimeout("g1", 3, at(10));
        timer.schedulePhaseTimeout("g1", 4, at(10));
        timer.schedulePhaseTimeout("g2", 3, at(10));
        timer.scheduleCleanup("g1", at(10));

        advance(10);

        assertThat(handler.calls).containsExactlyInAnyOrder("phase g1 v3", "phase g1 v4", "phase g2 v3", "cleanup g1");
    }

    // ---------- 8. 실행 중 다시 예약 ----------

    @Test
    void 실행_중에_같은_페이즈를_다시_예약하면_그_예약은_남아_다시_실행된다() {
        // GameFlowService.onPhaseTimeout이 게임 잠금을 못 잡으면 같은 버전을 1초 뒤로 다시 예약한다
        handler.onPhase = call -> {
            if (handler.calls.size() == 1) {
                timer.schedulePhaseTimeout("g1", 3, clock.instant().plusSeconds(1));
            }
        };
        timer.schedulePhaseTimeout("g1", 3, at(30));

        advance(30);
        assertThat(handler.calls).containsExactly("phase g1 v3");
        advance(1);

        assertThat(handler.calls).containsExactly("phase g1 v3", "phase g1 v3");
    }

    @Test
    void 실행_중에_같은_정리를_다시_예약해도_남는다() {
        handler.onCleanup = call -> {
            if (handler.calls.size() == 1) {
                timer.scheduleCleanup("g1", clock.instant().plusSeconds(1));
            }
        };
        timer.scheduleCleanup("g1", at(60));

        advance(60);
        advance(1);

        assertThat(handler.calls).containsExactly("cleanup g1", "cleanup g1");
    }

    // ---------- 9. 실패 ----------

    @Test
    void 실행이_예외로_끝나도_밖으로_던지지_않고_다른_예약은_실행한다() {
        handler.onPhase = call -> {
            if (call.equals("phase g1 v3")) {
                throw new IllegalStateException("판정 버그");
            }
        };
        timer.schedulePhaseTimeout("g1", 3, at(10));
        timer.schedulePhaseTimeout("g2", 1, at(10));
        timer.scheduleCleanup("g3", at(11));

        clock.advance(Duration.ofSeconds(11));
        assertThatCode(this::fireDue).doesNotThrowAnyException();

        assertThat(handler.calls).containsExactlyInAnyOrder("phase g1 v3", "phase g2 v1", "cleanup g3");
    }

    /** 받은 호출을 "phase g1 v3" / "cleanup g1" 문자열로 기록한다. 기록한 뒤 onPhase/onCleanup을 실행한다. */
    private static final class RecordingHandler implements GameTimeoutHandler {
        private final List<String> calls = new ArrayList<>();
        private Consumer<String> onPhase = call -> { };
        private Consumer<String> onCleanup = call -> { };

        @Override
        public void onPhaseTimeout(String gameId, long phaseVersion) {
            String call = "phase " + gameId + " v" + phaseVersion;
            calls.add(call);
            onPhase.accept(call);
        }

        @Override
        public void onCleanup(String gameId) {
            String call = "cleanup " + gameId;
            calls.add(call);
            onCleanup.accept(call);
        }
    }
}
