package com.WhoisntCitizen_server.game.scheduling;

import org.slf4j.Logger;
import org.slf4j.LoggerFactory;
import org.springframework.scheduling.TaskScheduler;

import java.time.Instant;
import java.util.Map;
import java.util.concurrent.ConcurrentHashMap;
import java.util.concurrent.atomic.AtomicLong;
import java.util.function.Supplier;

/**
 * 서버 메모리의 TaskScheduler로 예약하는 GameTimer. (서버 1대 기준, 서버가 꺼지면 예약도 사라진다)
 * handler는 Supplier로 받는다. GameFlowService가 GameTimer를 주입받고 동시에 handler이기도 해서,
 * 생성 시점에 바로 받으면 순환 의존이 생기기 때문이다. 실제 호출 시점에 꺼내 쓴다.
 *
 * 같은 대상을 다시 예약하면 마지막 예약만 실행한다. (GameTimer 계약 6번)
 * 스케줄러에 걸린 작업은 취소하지 않고, 대상마다 "마지막 예약 번호"를 기억해 두었다가 실행할 때 번호가 다르면 건너뛴다.
 * 실행이 끝나면 그 번호일 때만 기록을 지운다. 실행 중에 다시 예약했으면 번호가 바뀌어 새 예약이 남는다. (계약 8번)
 * Redis 구현에서 "점수가 가져갈 때와 같을 때만 지운다"와 같은 규칙이다.
 */
public class LocalGameTimer implements GameTimer {

    private static final Logger log = LoggerFactory.getLogger(LocalGameTimer.class);

    private final TaskScheduler scheduler;
    private final Supplier<GameTimeoutHandler> handler;
    /** 대상(TimerKeys) → 마지막 예약 번호. 실행이 끝나면 지운다 */
    private final Map<String, Long> latest = new ConcurrentHashMap<>();
    private final AtomicLong sequence = new AtomicLong();

    public LocalGameTimer(TaskScheduler scheduler, Supplier<GameTimeoutHandler> handler) {
        this.scheduler = scheduler;
        this.handler = handler;
    }

    @Override
    public void schedulePhaseTimeout(String gameId, long phaseVersion, Instant at) {
        schedule(TimerKeys.phase(gameId, phaseVersion), at, () -> handler.get().onPhaseTimeout(gameId, phaseVersion));
    }

    @Override
    public void scheduleCleanup(String gameId, Instant at) {
        schedule(TimerKeys.cleanup(gameId), at, () -> handler.get().onCleanup(gameId));
    }

    private void schedule(String key, Instant at, Runnable action) {
        long token = sequence.incrementAndGet();
        latest.put(key, token);
        scheduler.schedule(() -> fire(key, token, action), at);
    }

    private void fire(String key, long token, Runnable action) {
        Long current = latest.get(key);
        if (current == null || current != token) {
            return; // 그 뒤에 다시 예약됐다 (새 예약이 실행된다)
        }
        try {
            action.run();
        } catch (RuntimeException e) {
            // 밖으로 던지면 스케줄러가 로그만 남기거나(운영) 다른 예약 실행을 막는다(테스트). 계약 9번
            log.error("게임 타이머 실행 실패: {}", key, e);
        } finally {
            latest.remove(key, token); // 실행 중에 다시 예약했으면 번호가 달라 지우지 않는다
        }
    }
}
