package com.WhoisntCitizen_server.game.scheduling.redis;

import com.WhoisntCitizen_server.game.repository.redis.GameRedis;
import com.WhoisntCitizen_server.game.scheduling.GameTimeoutHandler;
import com.WhoisntCitizen_server.game.scheduling.GameTimer;
import com.WhoisntCitizen_server.game.scheduling.TimerKeys;
import com.WhoisntCitizen_server.game.scheduling.TimerTarget;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;
import org.springframework.data.redis.core.StringRedisTemplate;
import org.springframework.data.redis.core.script.DefaultRedisScript;
import org.springframework.data.redis.core.script.RedisScript;

import java.time.Clock;
import java.time.Duration;
import java.time.Instant;
import java.util.ArrayList;
import java.util.List;
import java.util.Objects;
import java.util.Optional;
import java.util.function.Supplier;

/**
 * Redis에 예약을 보관하는 GameTimer. 게임 그룹 Redis(GameRedis)를 쓴다.
 * 서버가 꺼져도 예약이 남고, 어느 서버든 시간이 된 예약을 가져가 실행할 수 있다.
 *
 * 키 구조
 *  - game:v1:timers  (Sorted Set)  member = 예약 대상 이름(TimerKeys), score = 실행할 시각(epoch ms)
 *    예) phase:abc:3 → 1791631095000,  cleanup:abc → 1791631155000
 *
 * 1) 예약: ZADD 한 번. member가 대상 이름이라 같은 대상을 다시 예약하면 점수(시각)만 바뀐다. (GameTimer 계약 6번)
 *
 * 2) 가져가기 (pollOnce → claimDue, Lua 스크립트 CLAIM)
 *    점수가 지금 이하인 예약을 꺼내면서, 그 점수를 "지금 + lease(30초)"로 바꾼다. 꺼내기와 점수 바꾸기가 한 스크립트라
 *    Redis 안에서 한 번에 실행된다. 그래서 여러 서버가 동시에 확인해도 한 예약은 한 서버만 가져간다.
 *    (먼저 가져간 서버가 점수를 미래로 밀어 두었으니, 다음 서버의 "점수가 지금 이하인 것"에 걸리지 않는다)
 *
 * 3) 실행: 이름을 TimerTarget으로 되돌려 받는 쪽(GameTimeoutHandler)을 부른다. 실패해도 다음 예약은 계속 실행한다.
 *
 * 4) 완료 (Lua 스크립트 COMPLETE)
 *    점수가 "내가 가져갈 때 바꾼 값" 그대로일 때만 지운다.
 *     - 실행 중에 같은 대상을 다시 예약했으면(잠금 실패 후 1초 뒤 재시도) 점수가 바뀌어 있으므로 지우지 않는다. (계약 8번)
 *     - 실행이 lease보다 오래 걸려 다른 서버가 다시 가져갔으면 그 서버의 점수라 지우지 않는다.
 *
 * 서버가 실행 도중 죽으면: 완료하지 못한 예약의 점수(지금 + lease)가 30초 뒤 과거가 되어 다른 서버가 다시 가져간다.
 * 그래서 "적어도 한 번" 실행된다. 드물게 두 번 실행될 수 있지만 받는 쪽이 버전으로 지난 페이즈를 무시하므로 안전하다.
 *
 * 실패한 실행은 다시 시도하지 않고 지운다(서버 메모리 구현과 같다). 같은 버그로 30초마다 계속 실패하지 않게 하기 위해서다.
 * 받는 쪽(GameFlowService)은 자기 실패를 직접 처리한다. (잠금 실패는 재예약, 판정 오류는 게임 취소)
 *
 * 점수는 각 서버의 Clock으로 계산한다. 서버끼리 시계가 어긋나면 그만큼 일찍·늦게 실행된다. (보통 NTP로 맞춰져 있다)
 * 실행하는 방법은 두 가지다.
 *  - pollOnce(): 가져온 예약을 부른 스레드에서 차례로 실행한다. 테스트(타이머 계약)와 점검용.
 *  - claimDue(limit) + runClaimed(c): 가져가기와 실행을 나눈다. 서버에서는 RedisTimerDispatcher가
 *    빈 작업 스레드 수만큼만 가져가 예약마다 다른 스레드에서 실행한다. (한 게임이 잠금을 기다려도 다른 게임은 실행된다)
 */
public class RedisGameTimer implements GameTimer {

    static final String TIMERS_KEY = "game:v1:timers";
    /** 가져간 예약을 다른 서버가 다시 가져가지 못하게 막아 두는 시간. 실행이 이보다 오래 걸리면 다른 서버가 다시 실행한다 */
    public static final Duration DEFAULT_LEASE = Duration.ofSeconds(30);
    /** 한 번 확인할 때 가져가는 최대 개수. 밀린 예약이 많아도 한 번의 확인이 너무 길어지지 않게 한다 */
    public static final int DEFAULT_BATCH_SIZE = 100;
    /**
     * 예약 시각보다 이만큼 넘게 늦게 실행되면 경고를 남긴다. 확인 간격(기본 100ms)과 실행 시간을 생각하면 평소에는 넘지 않는다.
     * 자주 보이면 작업 스레드(workers)가 모자라거나 게임 잠금이 오래 막히는 것이다.
     */
    static final Duration LATE_WARN = Duration.ofSeconds(1);

    private static final Logger log = LoggerFactory.getLogger(RedisGameTimer.class);

    /**
     * 시간이 된 예약을 가져간다. KEYS[1] = timers, ARGV[1] = 지금(ms), ARGV[2] = 가져간 뒤 점수(지금 + lease), ARGV[3] = 최대 개수
     * 점수 순(오래된 것부터)으로 [member1, 원래 점수1, member2, 원래 점수2, ...]를 돌려준다.
     * 원래 점수(예약 시각)는 점수를 lease로 바꾸기 전에 읽어 둔 값이다. 얼마나 늦게 실행되는지 재는 데 쓴다.
     */
    @SuppressWarnings("rawtypes")
    private static final RedisScript<List> CLAIM = new DefaultRedisScript<>(
            "local due = redis.call('ZRANGEBYSCORE', KEYS[1], '-inf', ARGV[1], 'WITHSCORES', 'LIMIT', 0, tonumber(ARGV[3]))\n"
                    + "for i = 1, #due, 2 do\n"
                    + "  redis.call('ZADD', KEYS[1], ARGV[2], due[i])\n"
                    + "end\n"
                    + "return due",
            List.class);

    /**
     * 점수가 가져갈 때 바꾼 값 그대로일 때만 지운다. KEYS[1] = timers, ARGV[1] = member, ARGV[2] = 가져갈 때 바꾼 점수
     * 지웠으면 1, 아니면 0.
     */
    private static final RedisScript<Long> COMPLETE = new DefaultRedisScript<>(
            "local score = redis.call('ZSCORE', KEYS[1], ARGV[1])\n"
                    + "if score and tonumber(score) == tonumber(ARGV[2]) then\n"
                    + "  redis.call('ZREM', KEYS[1], ARGV[1])\n"
                    + "  return 1\n"
                    + "end\n"
                    + "return 0",
            Long.class);

    private final StringRedisTemplate redis;
    private final Supplier<GameTimeoutHandler> handler;
    private final Clock clock;
    private final Duration lease;
    private final int batchSize;

    public RedisGameTimer(GameRedis gameRedis, Supplier<GameTimeoutHandler> handler, Clock clock) {
        this(gameRedis, handler, clock, DEFAULT_LEASE, DEFAULT_BATCH_SIZE);
    }

    /**
     * handler는 Supplier로 받는다. GameFlowService가 GameTimer를 주입받고 동시에 handler이기도 해서,
     * 생성 시점에 바로 받으면 순환 의존이 생기기 때문이다. (LocalGameTimer와 같다)
     */
    public RedisGameTimer(GameRedis gameRedis, Supplier<GameTimeoutHandler> handler, Clock clock,
                          Duration lease, int batchSize) {
        this.redis = Objects.requireNonNull(gameRedis, "gameRedis").template();
        this.handler = Objects.requireNonNull(handler, "handler");
        this.clock = Objects.requireNonNull(clock, "clock");
        this.lease = Objects.requireNonNull(lease, "lease");
        if (lease.isNegative() || lease.isZero()) {
            throw new IllegalArgumentException("lease는 0보다 커야 합니다: " + lease);
        }
        if (batchSize <= 0) {
            throw new IllegalArgumentException("batchSize는 0보다 커야 합니다: " + batchSize);
        }
        this.batchSize = batchSize;
    }

    // ---------- 1) 예약 ----------

    @Override
    public void schedulePhaseTimeout(String gameId, long phaseVersion, Instant at) {
        schedule(TimerKeys.phase(gameId, phaseVersion), at);
    }

    @Override
    public void scheduleCleanup(String gameId, Instant at) {
        schedule(TimerKeys.cleanup(gameId), at);
    }

    private void schedule(String key, Instant at) {
        Objects.requireNonNull(at, "at");
        redis.opsForZSet().add(TIMERS_KEY, key, at.toEpochMilli()); // 있으면 점수만 바뀐다
    }

    // ---------- 2~4) 가져가기 · 실행 · 완료 ----------

    /**
     * 지금 시각까지 된 예약을 최대 batchSize개 가져가 실행한다. 실행한 예약 수를 돌려준다.
     * 3-4의 주기 작업이 짧은 간격으로 부른다. 밀린 예약이 batchSize보다 많으면 나머지는 다음 확인 때 실행된다.
     * 실행 중 예외는 밖으로 던지지 않는다. (Redis 연결 실패처럼 가져가기 자체가 실패하면 그 예외는 던진다)
     */
    public int pollOnce() {
        List<Claimed> claimed = claimDue();
        for (Claimed c : claimed) {
            runClaimed(c);
        }
        return claimed.size();
    }

    /** 시간이 된 예약을 최대 batchSize개 가져간다. */
    List<Claimed> claimDue() {
        return claimDue(batchSize);
    }

    /**
     * 시간이 된 예약을 최대 limit개(batchSize를 넘지 않음) 가져간다. 점수를 "지금 + lease"로 바꿔 다른 서버가 가져가지 못하게 한다.
     * 가져간 예약은 lease 안에 runClaimed로 실행해야 한다. (그러지 못하면 lease 뒤 다른 서버가 다시 실행한다)
     */
    List<Claimed> claimDue(int limit) {
        int max = Math.min(limit, batchSize);
        if (max <= 0) {
            return List.of();
        }
        long now = clock.millis();
        long leaseUntil = now + lease.toMillis();
        List<?> pairs = redis.execute(CLAIM, List.of(TIMERS_KEY),
                String.valueOf(now), String.valueOf(leaseUntil), String.valueOf(max));
        if (pairs == null || pairs.isEmpty()) {
            return List.of();
        }
        List<Claimed> claimed = new ArrayList<>(pairs.size() / 2);
        for (int i = 0; i + 1 < pairs.size(); i += 2) {
            String member = String.valueOf(pairs.get(i));
            long dueAt = (long) Double.parseDouble(String.valueOf(pairs.get(i + 1)));
            claimed.add(new Claimed(member, dueAt, leaseUntil));
        }
        return claimed;
    }

    /**
     * 가져간 예약 하나를 실행하고 완료 처리한다. 예외를 밖으로 던지지 않는다.
     * 완료(Redis)가 실패하면 예약이 남아 lease 뒤 다시 실행된다. (받는 쪽이 버전으로 걸러낸다)
     */
    void runClaimed(Claimed c) {
        fire(c);
        try {
            complete(c);
        } catch (RuntimeException e) {
            log.warn("게임 타이머 완료 처리 실패. lease 뒤 다시 실행될 수 있습니다: {} ({})", c.key(), e.getMessage());
        }
    }

    private void fire(Claimed c) {
        warnIfLate(c);
        TimerTarget target;
        try {
            target = TimerKeys.parse(c.key());
        } catch (IllegalArgumentException e) {
            // 다른 프로그램이 넣은 값이나 예전 형식. 실행할 수 없으니 complete에서 지운다
            log.warn("알 수 없는 타이머 예약을 지웁니다: {} ({})", c.key(), e.getMessage());
            return;
        }
        try {
            target.fire(handler.get());
        } catch (RuntimeException e) {
            log.error("게임 타이머 실행 실패: {}", c.key(), e);
        }
    }

    /**
     * 예약 시각보다 LATE_WARN 넘게 늦게 실행되면 경고를 남긴다. 실제로 밀리는지 숫자로 보기 위한 것이다.
     * lease가 지나 다시 가져간 예약(실행 중 서버가 죽은 경우)은 "다시 실행할 수 있게 된 시각"부터 잰다.
     */
    private void warnIfLate(Claimed c) {
        long lateMillis = lateMillis(c, clock.millis());
        if (lateMillis > LATE_WARN.toMillis()) {
            log.warn("게임 타이머가 예약 시각보다 {}ms 늦게 실행됩니다: {} (작업 스레드가 모자라거나 게임 잠금이 오래 막혔을 수 있습니다)",
                    lateMillis, c.key());
        }
    }

    /** 예약 시각보다 얼마나 늦었는지(ms). 일찍 실행되는 일은 없지만 서버 시계가 어긋나면 음수가 될 수 있어 0으로 맞춘다. */
    static long lateMillis(Claimed c, long nowMillis) {
        return Math.max(0, nowMillis - c.dueAt());
    }

    /** 점수가 가져갈 때 바꾼 값 그대로일 때만 지운다. 지웠으면 true. */
    boolean complete(Claimed c) {
        Long removed = redis.execute(COMPLETE, List.of(TIMERS_KEY), c.key(), String.valueOf(c.leaseUntil()));
        return removed != null && removed == 1L;
    }

    /**
     * 가져간 예약.
     * @param dueAt      가져가기 전의 점수 = 예약 시각(epoch ms). 늦게 실행되는지 재는 데 쓴다
     * @param leaseUntil 가져갈 때 바꿔 둔 점수. 완료할 때 이 값인지 확인한다
     */
    record Claimed(String key, long dueAt, long leaseUntil) {
    }

    // ---------- 테스트·점검용 ----------

    /** 대상이 예약된 시각. 가져간 뒤 실행 중이면 "지금 + lease"가 보인다. 예약이 없으면 비어 있다. */
    Optional<Instant> scheduledAt(String key) {
        Double score = redis.opsForZSet().score(TIMERS_KEY, key);
        return Optional.ofNullable(score).map(s -> Instant.ofEpochMilli(s.longValue()));
    }

    /** 남아 있는 예약 수 */
    long size() {
        Long size = redis.opsForZSet().zCard(TIMERS_KEY);
        return size == null ? 0 : size;
    }
}
