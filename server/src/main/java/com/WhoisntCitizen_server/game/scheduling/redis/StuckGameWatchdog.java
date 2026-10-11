package com.WhoisntCitizen_server.game.scheduling.redis;

import com.WhoisntCitizen_server.game.entity.Game;
import com.WhoisntCitizen_server.game.repository.GameRepository;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;
import org.springframework.context.SmartLifecycle;
import org.springframework.scheduling.TaskScheduler;

import java.time.Clock;
import java.time.Duration;
import java.time.Instant;
import java.util.Objects;
import java.util.Optional;
import java.util.concurrent.ScheduledFuture;

/**
 * 멈춘 게임 감시. Redis 게임 타이머를 쓸 때, 페이즈가 끝날 시각이 지났는데 그 페이즈의 타이머 예약이 없는 게임을 찾아 다시 건다.
 *
 * 왜 필요한가
 *  페이즈를 바꿀 때 "게임 상태 저장"과 "다음 타이머 예약"은 Redis에 보내는 서로 다른 명령이다. (GameFlowService.moveTo)
 *  저장 직후·예약 전에 서버가 죽으면, 상태는 "12:00:30에 밤이 끝난다"인데 그 밤을 끝낼 예약이 없어 게임이 영원히 멈춘다.
 *  예약 자체가 Redis에 남는 Redis 타이머에서도 이 틈은 남아 있어서, 주기적으로 확인해 메운다.
 *  (서버 메모리 타이머에서는 재시작할 때 GameTimerRecovery가 같은 일을 한다)
 *
 * 동작 (CHECK_INTERVAL마다, 서버마다)
 *  - 진행 중인 게임(findActiveIds)을 하나씩 읽는다. 잠금 없이 읽는다. 바꾸는 것은 타이머 예약뿐이라 잠금이 필요 없다.
 *  - 끝날 시각(phaseEndsAt) + GRACE가 지났는데 그 버전의 페이즈 타이머가 없으면 "지금"으로 예약한다. (ZADD NX)
 *    곧 확인 작업이 가져가 페이즈를 넘긴다.
 *  - 예약이 이미 있으면 아무것도 하지 않는다. 늦게 실행되는 중이거나(작업 스레드가 바쁨) 다른 서버가 실행 중일 수 있다.
 *  - 여러 서버가 동시에 감시해도 NX라서 예약은 하나만 생긴다. 읽은 직후 페이즈가 넘어가 옛 버전을 예약하더라도,
 *    그 타이머는 실행될 때 버전이 달라 무시된다.
 *
 * 다루지 않는 것
 *  - 시작 전 게임(phase 없음): 게임을 저장한 직후·첫 밤 시작 전에 서버가 죽은 경우다. 걸 타이머가 없어 경고만 남긴다.
 *  - 끝난 게임의 정리 타이머: 끝난 게임은 진행 중 목록에 없다. 상태 키에 TTL(endedTtl)이 있어 결국 지워진다.
 */
public class StuckGameWatchdog implements SmartLifecycle {

    /** 감시 주기 */
    static final Duration CHECK_INTERVAL = Duration.ofSeconds(30);
    /** 끝날 시각이 이만큼 지나도 예약이 없을 때만 멈춘 것으로 본다 (저장과 예약 사이의 짧은 순간을 멈춘 것으로 보지 않게) */
    static final Duration GRACE = Duration.ofSeconds(10);

    private static final Logger log = LoggerFactory.getLogger(StuckGameWatchdog.class);

    private final GameRepository gameRepository;
    private final RedisGameTimer timer;
    private final TaskScheduler scheduler;
    private final Clock clock;

    private volatile boolean running;
    /** start할 때마다 바뀐다. 멈췄다 다시 켰을 때 예전 예약 때문에 감시가 두 줄로 돌지 않게 한다 (RedisTimerPoller와 같다) */
    private volatile long generation;
    private volatile ScheduledFuture<?> next;

    public StuckGameWatchdog(GameRepository gameRepository, RedisGameTimer timer, TaskScheduler scheduler, Clock clock) {
        this.gameRepository = Objects.requireNonNull(gameRepository, "gameRepository");
        this.timer = Objects.requireNonNull(timer, "timer");
        this.scheduler = Objects.requireNonNull(scheduler, "scheduler");
        this.clock = Objects.requireNonNull(clock, "clock");
    }

    // ---------- 감시 ----------

    /** 진행 중인 게임을 한 번 훑어 멈춘 게임의 타이머를 다시 건다. 다시 건 게임 수를 돌려준다. 한 게임이 실패해도 나머지는 계속한다. */
    public int checkOnce() {
        int rescued = 0;
        for (String gameId : gameRepository.findActiveIds()) {
            try {
                if (rescueIfStuck(gameId)) {
                    rescued++;
                }
            } catch (RuntimeException e) {
                log.warn("[{}] 멈춘 게임 확인 실패: {}", gameId, e.getMessage());
            }
        }
        return rescued;
    }

    /** 게임 하나가 멈췄으면 타이머를 다시 건다. 다시 걸었으면 true. */
    boolean rescueIfStuck(String gameId) {
        Optional<Game> found = gameRepository.findById(gameId);
        if (found.isEmpty() || found.get().isEnded()) {
            return false; // 목록을 받은 뒤 끝났거나 지워짐
        }
        Game game = found.get();
        if (game.getPhase() == null || game.getPhaseEndsAt() == null) {
            log.warn("[{}] 첫 밤을 시작하기 전 상태로 남아 있습니다. 걸 타이머가 없어 진행되지 않습니다", gameId);
            return false;
        }
        Instant now = clock.instant();
        if (game.getPhaseEndsAt().plus(GRACE).isAfter(now)) {
            return false; // 아직 끝날 시각이 안 됐거나, 막 지난 참
        }
        boolean added = timer.schedulePhaseTimeoutIfAbsent(gameId, game.getPhaseVersion(), now);
        if (added) {
            log.warn("[{}] {} 페이즈가 끝날 시각({})이 지났는데 타이머 예약이 없어 다시 걸었습니다 (v{})",
                    gameId, game.getPhase(), game.getPhaseEndsAt(), game.getPhaseVersion());
        }
        return added;
    }

    // ---------- 켜고 끄기 (SmartLifecycle) ----------

    @Override
    public synchronized void start() {
        if (running) {
            return;
        }
        running = true;
        generation++;
        log.info("멈춘 게임 감시 시작: {}초마다", CHECK_INTERVAL.toSeconds());
        scheduleNext(generation);
    }

    @Override
    public synchronized void stop() {
        if (!running) {
            return;
        }
        running = false;
        ScheduledFuture<?> pending = next;
        if (pending != null) {
            pending.cancel(false);
        }
    }

    @Override
    public boolean isRunning() {
        return running;
    }

    private void scheduleNext(long gen) {
        next = scheduler.schedule(() -> tick(gen), clock.instant().plus(CHECK_INTERVAL));
    }

    void tick(long gen) {
        if (!running || gen != generation) {
            return;
        }
        try {
            checkOnce();
        } catch (RuntimeException e) {
            log.warn("멈춘 게임 감시 실패. 다음 주기에 다시 확인합니다: {}", e.getMessage());
        } finally {
            if (running && gen == generation) {
                scheduleNext(gen);
            }
        }
    }
}
