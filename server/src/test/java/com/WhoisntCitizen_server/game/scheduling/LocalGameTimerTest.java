package com.WhoisntCitizen_server.game.scheduling;

import com.WhoisntCitizen_server.support.ManualTaskScheduler;
import com.WhoisntCitizen_server.support.MutableClock;
import org.junit.jupiter.api.BeforeEach;
import org.junit.jupiter.api.Test;

import java.time.Duration;
import java.time.Instant;
import java.util.ArrayList;
import java.util.List;

import static org.assertj.core.api.Assertions.assertThat;

class LocalGameTimerTest {

    private static final Instant NOW = Instant.parse("2026-10-07T12:00:00Z");

    private final List<String> calls = new ArrayList<>();
    private ManualTaskScheduler scheduler;
    private LocalGameTimer timer;

    @BeforeEach
    void setUp() {
        scheduler = new ManualTaskScheduler(new MutableClock(NOW));
        GameTimeoutHandler handler = new GameTimeoutHandler() {
            @Override
            public void onPhaseTimeout(String gameId, long phaseVersion) {
                calls.add("timeout " + gameId + " v" + phaseVersion);
            }

            @Override
            public void onCleanup(String gameId) {
                calls.add("cleanup " + gameId);
            }
        };
        timer = new LocalGameTimer(scheduler, () -> handler);
    }

    @Test
    void 예약_시각이_되면_페이즈_종료를_알린다() {
        timer.schedulePhaseTimeout("g1", 3, NOW.plusSeconds(30));

        scheduler.advance(Duration.ofSeconds(29));
        assertThat(calls).isEmpty();

        scheduler.advance(Duration.ofSeconds(1));
        assertThat(calls).containsExactly("timeout g1 v3");
    }

    @Test
    void 정리_예약도_시각_순서대로_실행된다() {
        timer.scheduleCleanup("g1", NOW.plusSeconds(60));
        timer.schedulePhaseTimeout("g2", 1, NOW.plusSeconds(5));

        scheduler.advance(Duration.ofSeconds(60));

        assertThat(calls).containsExactly("timeout g2 v1", "cleanup g1");
    }

    @Test
    void handler는_예약할_때가_아니라_실행할_때_꺼낸다() {
        List<GameTimeoutHandler> holder = new ArrayList<>();
        LocalGameTimer lazy = new LocalGameTimer(scheduler, () -> holder.get(0));
        lazy.schedulePhaseTimeout("g1", 1, NOW.plusSeconds(1)); // 아직 handler 없음

        holder.add(new GameTimeoutHandler() {
            @Override
            public void onPhaseTimeout(String gameId, long phaseVersion) {
                calls.add("late " + gameId);
            }

            @Override
            public void onCleanup(String gameId) {
            }
        });
        scheduler.advance(Duration.ofSeconds(1));

        assertThat(calls).containsExactly("late g1");
    }
}
