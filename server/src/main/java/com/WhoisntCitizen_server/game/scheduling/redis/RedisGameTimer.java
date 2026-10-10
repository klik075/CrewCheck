package com.WhoisntCitizen_server.game.scheduling.redis;

import com.WhoisntCitizen_server.game.repository.redis.GameRedis;
import com.WhoisntCitizen_server.game.scheduling.GameTimer;
import com.WhoisntCitizen_server.game.scheduling.TimerKeys;
import org.springframework.data.redis.core.StringRedisTemplate;

import java.time.Instant;
import java.util.Objects;
import java.util.Optional;

/**
 * Redis에 예약을 보관하는 GameTimer. 게임 그룹 Redis(GameRedis)를 쓴다.
 * 서버가 꺼져도 예약이 남고, 어느 서버든 시간이 된 예약을 가져가 실행할 수 있다.
 *
 * 키 구조
 *  - game:v1:timers  (Sorted Set)  member = 예약 대상 이름(TimerKeys), score = 실행할 시각(epoch ms)
 *    예) phase:abc:3 → 1791631095000,  cleanup:abc → 1791631155000
 *
 * 예약은 ZADD 한 번이다. member가 대상 이름이라 같은 대상을 다시 예약하면 점수(시각)만 바뀐다.
 * → "같은 대상은 마지막 예약 하나만 남는다"(GameTimer 계약 6번)를 Redis가 그대로 지켜 준다.
 *
 * 지금(3-2)은 예약만 한다. 시간이 된 예약을 가져가 실행하는 부분은 3-3에서 추가한다.
 *
 * 점수는 예약한 서버가 계산한 시각이다. (게임의 phaseEndsAt과 같은 Clock 기준)
 * 서버끼리 시계가 어긋나면 그만큼 일찍·늦게 실행된다. 보통 NTP로 맞춰져 있어 문제가 되지 않는다.
 */
public class RedisGameTimer implements GameTimer {

    static final String TIMERS_KEY = "game:v1:timers";

    private final StringRedisTemplate redis;

    public RedisGameTimer(GameRedis gameRedis) {
        this.redis = Objects.requireNonNull(gameRedis, "gameRedis").template();
    }

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

    /** 대상이 예약된 시각. 예약이 없으면 비어 있다. (테스트·점검용) */
    Optional<Instant> scheduledAt(String key) {
        Double score = redis.opsForZSet().score(TIMERS_KEY, key);
        return Optional.ofNullable(score).map(s -> Instant.ofEpochMilli(s.longValue()));
    }

    /** 남아 있는 예약 수. (테스트·점검용) */
    long size() {
        Long size = redis.opsForZSet().zCard(TIMERS_KEY);
        return size == null ? 0 : size;
    }
}
