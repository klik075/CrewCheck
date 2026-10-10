package com.WhoisntCitizen_server.game.scheduling;

import com.WhoisntCitizen_server.support.GameTimerContractTest;
import com.WhoisntCitizen_server.support.ManualTaskScheduler;
import com.WhoisntCitizen_server.support.MutableClock;
import org.junit.jupiter.api.Test;

import java.util.ArrayList;
import java.util.List;

import static org.assertj.core.api.Assertions.assertThat;

/**
 * 서버 메모리 게임 타이머가 타이머 계약(GameTimerContractTest)을 지키는지 확인한다.
 * 스케줄러는 ManualTaskScheduler라서 fireDue()는 "지금 시각까지 된 작업 실행"이다.
 */
class LocalGameTimerTest extends GameTimerContractTest {

    private ManualTaskScheduler scheduler;

    @Override
    protected GameTimer newTimer(MutableClock clock, GameTimeoutHandler handler) {
        scheduler = new ManualTaskScheduler(clock);
        return new LocalGameTimer(scheduler, () -> handler);
    }

    @Override
    protected void fireDue() {
        scheduler.runDue();
    }

    // ---------- 서버 메모리 구현만의 동작 ----------

    @Test
    void handler는_예약할_때가_아니라_실행할_때_꺼낸다() {
        // GameFlowService와 GameTimer가 서로를 주입받는 순환을 피하려고 handler를 Supplier로 받는다
        List<String> calls = new ArrayList<>();
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
        clock.advance(java.time.Duration.ofSeconds(1));
        fireDue();

        assertThat(calls).containsExactly("late g1");
    }
}
