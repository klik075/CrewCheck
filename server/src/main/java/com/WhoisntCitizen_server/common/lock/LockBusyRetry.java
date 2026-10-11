package com.WhoisntCitizen_server.common.lock;

import com.WhoisntCitizen_server.common.exception.LockTimeoutException;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;
import org.springframework.beans.factory.annotation.Qualifier;
import org.springframework.scheduling.TaskScheduler;
import org.springframework.stereotype.Component;

import java.time.Clock;
import java.time.Duration;

/**
 * 잠금을 못 잡아(LockTimeoutException) 실패한 일을 조금 뒤 다시 시도한다. 이벤트 리스너가 쓴다.
 *
 * 왜 리스너 안에서 다시 시도하나
 *  - 게임 종료 이벤트(GameEndedEvent)는 방 복귀(RoomGameListener)와 전적 저장(UserStatsListener)이 함께 받는다.
 *    이벤트를 통째로 다시 발행하면 이미 성공한 전적 저장이 한 번 더 실행돼 승패가 두 번 쌓인다.
 *  - 그래서 실패한 리스너 하나만, 실패한 그 일만 다시 시도한다.
 *
 * 규칙
 *  - 잠금 대기 시간 초과만 다시 시도한다. 다른 예외는 다시 해도 똑같이 실패하므로 그대로 던진다(첫 시도) / 로그만 남긴다(다시 시도).
 *  - 다시 시도하는 일은 여러 번 실행돼도 결과가 같아야 한다. (방 복귀·플레이어 제외·방 삭제는 잠금 안에서 상태를 다시 확인한다)
 *  - 최대 MAX_ATTEMPTS번(처음 포함)까지 RETRY_DELAY 간격으로 시도하고, 그래도 안 되면 포기하고 error 로그를 남긴다.
 */
@Component
public class LockBusyRetry {

    static final int MAX_ATTEMPTS = 3;
    static final Duration RETRY_DELAY = Duration.ofSeconds(1);

    private static final Logger log = LoggerFactory.getLogger(LockBusyRetry.class);

    private final TaskScheduler scheduler;
    private final Clock clock;

    public LockBusyRetry(@Qualifier("gamePhaseScheduler") TaskScheduler scheduler, Clock clock) {
        this.scheduler = scheduler;
        this.clock = clock;
    }

    /**
     * action을 지금 실행한다. 잠금을 못 잡았으면 RETRY_DELAY 뒤 다시 시도하도록 예약하고 바로 돌아온다.
     * @param description 로그에 남길 설명 (예: "[game-1] 방 3 대기 상태 복귀")
     */
    public void run(String description, Runnable action) {
        attempt(description, action, 1);
    }

    private void attempt(String description, Runnable action, int attempt) {
        try {
            action.run();
        } catch (LockTimeoutException e) {
            if (attempt >= MAX_ATTEMPTS) {
                log.error("{}: 잠금을 {}번 잡지 못해 포기합니다. ({})", description, attempt, e.getMessage());
                return;
            }
            log.warn("{}: 잠금을 잡지 못해 {}초 뒤 다시 시도합니다. ({}/{})",
                    description, RETRY_DELAY.toSeconds(), attempt, MAX_ATTEMPTS);
            scheduler.schedule(() -> retry(description, action, attempt + 1), clock.instant().plus(RETRY_DELAY));
        }
    }

    /** 다시 시도는 스케줄러 스레드에서 돈다. 잠금 말고 다른 이유로 실패하면 던질 곳이 없으니 로그로 남긴다. */
    private void retry(String description, Runnable action, int attempt) {
        try {
            attempt(description, action, attempt);
        } catch (RuntimeException e) {
            log.error("{}: 다시 시도하다 실패했습니다. ({}번째)", description, attempt, e);
        }
    }
}
