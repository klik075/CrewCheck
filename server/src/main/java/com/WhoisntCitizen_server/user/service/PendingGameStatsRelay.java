package com.WhoisntCitizen_server.user.service;

import com.WhoisntCitizen_server.game.result.GameResultService;
import lombok.extern.slf4j.Slf4j;
import org.springframework.beans.factory.annotation.Qualifier;
import org.springframework.beans.factory.annotation.Value;
import org.springframework.boot.context.event.ApplicationReadyEvent;
import org.springframework.context.event.EventListener;
import org.springframework.scheduling.TaskScheduler;
import org.springframework.stereotype.Component;

import java.time.Clock;
import java.time.Duration;
import java.util.List;
import java.util.OptionalInt;

/**
 * 전적 반영 재시도 (outbox 릴레이).
 * [서버 여러 대] 주기 작업 — 서버마다 돈다. 두 번 돌아도 안전: 반영 시각(stats_applied_at)을 조건부 UPDATE로 먼저 차지한 쪽만 전적을 올린다.
 * 게임 결과는 게임이 끝날 때 DB(game_results)에 남지만, 전적을 올리는 GameEndedEvent는 서버 메모리로만 전달된다.
 * 그래서 게임 종료 직후 서버가 꺼지거나 전적 저장이 실패하면 "결과는 있는데 전적에 반영 안 된" 게임이 남는다.
 * 서버가 켜질 때 한 번, 그 뒤 mafia.stats.relay-interval-seconds마다 이런 게임을 찾아 반영한다.
 *  - 막 끝난 게임(PENDING_GRACE 이내)은 건너뛴다. 전적 리스너(UserStatsListener)가 곧 반영할 게임이다.
 *  - 리스너와 겹치거나 서버 여러 대가 함께 돌아도 같은 게임은 한 번만 반영된다. (UserStatsService.applyGameResult)
 *  - 방은 이미 없을 수 있으므로 채팅 안내는 보내지 않는다.
 */
@Slf4j
@Component
public class PendingGameStatsRelay {

    /** 끝난 지 이 시간이 지나도 반영 안 된 게임만 다시 시도한다 */
    static final Duration PENDING_GRACE = Duration.ofSeconds(10);

    /** 한 번에 다시 시도하는 최대 게임 수. 남은 게임은 다음 확인 때 이어서 한다 */
    static final int BATCH_SIZE = 100;

    private final GameResultService gameResultService;
    private final UserStatsService userStatsService;
    private final TaskScheduler scheduler;
    private final Clock clock;
    private final long intervalSeconds;

    public PendingGameStatsRelay(GameResultService gameResultService,
                                 UserStatsService userStatsService,
                                 @Qualifier("gamePhaseScheduler") TaskScheduler scheduler,
                                 Clock clock,
                                 @Value("${mafia.stats.relay-interval-seconds:30}") long intervalSeconds) {
        this.gameResultService = gameResultService;
        this.userStatsService = userStatsService;
        this.scheduler = scheduler;
        this.clock = clock;
        this.intervalSeconds = intervalSeconds;
    }

    @EventListener(ApplicationReadyEvent.class)
    public void start() {
        if (intervalSeconds <= 0) {
            log.info("전적 반영 재시도 꺼짐 (mafia.stats.relay-interval-seconds={})", intervalSeconds);
            return;
        }
        // 첫 실행은 바로(서버가 켜질 때), 그 뒤로는 앞 실행이 끝나고 intervalSeconds마다
        scheduler.scheduleWithFixedDelay(this::relayPending, Duration.ofSeconds(intervalSeconds));
        log.info("전적 반영 재시도 시작: {}초마다", intervalSeconds);
    }

    /**
     * 반영이 늦어진 게임을 찾아 전적에 반영한다. 이번에 반영한 게임 수를 돌려준다.
     * 반복 작업에서 예외가 밖으로 나가면 이후 실행이 멈추므로 게임마다 예외를 잡는다.
     */
    public int relayPending() {
        List<String> pending;
        try {
            pending = gameResultService.findStatsPendingIds(clock.instant().minus(PENDING_GRACE), BATCH_SIZE);
        } catch (RuntimeException e) {
            log.error("반영 안 된 전적 조회 실패", e);
            return 0;
        }
        int applied = 0;
        for (String gameId : pending) {
            try {
                OptionalInt updated = userStatsService.applyGameResult(gameId);
                if (updated.isPresent()) {
                    applied++;
                    log.warn("[{}] 반영이 늦어진 전적을 반영했습니다: {}명", gameId, updated.getAsInt());
                }
            } catch (RuntimeException e) {
                log.error("[{}] 전적 반영 재시도 실패. 다음 확인 때 다시 시도합니다", gameId, e);
            }
        }
        return applied;
    }
}
