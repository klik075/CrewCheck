package com.WhoisntCitizen_server.game.scheduling.redis;

import org.springframework.boot.context.properties.ConfigurationProperties;
import org.springframework.boot.context.properties.bind.DefaultValue;

import java.time.Duration;

/**
 * Redis 게임 타이머 설정 (mafia.redis.game.timer.*). 게임 그룹 Redis 설정(mafia.redis.game.*) 아래에 둔다.
 *
 * @param pollIntervalMillis 시간이 된 예약을 확인하는 간격. 페이즈가 이만큼까지 늦게 넘어갈 수 있다. (기본 100ms)
 *                           줄이면 밀린 예약을 빨리 따라잡지만 한가할 때도 그만큼 자주 Redis에 묻는다. (확인 한 번 = Lua 1번)
 * @param leaseSeconds       가져간 예약을 다른 서버가 다시 가져가지 못하게 막는 시간.
 *                           실행 도중 서버가 죽으면 이 시간 뒤 다른 서버가 다시 실행한다. (기본 30초)
 * @param batchSize          한 번 확인할 때 가져가는 최대 개수. (기본 100)
 * @param workers            예약을 실행하는 작업 스레드 수. 서버 한 대에서 동시에 실행하는 타이머 수다.
 *                           한 게임이 잠금을 기다려도 나머지 스레드로 다른 게임을 실행한다. (기본 4)
 */
@ConfigurationProperties(prefix = "mafia.redis.game.timer")
public record RedisTimerProperties(
        @DefaultValue("100") long pollIntervalMillis,
        @DefaultValue("30") long leaseSeconds,
        @DefaultValue("100") int batchSize,
        @DefaultValue("4") int workers
) {

    public RedisTimerProperties {
        if (pollIntervalMillis <= 0) {
            throw new IllegalArgumentException("mafia.redis.game.timer.poll-interval-millis는 0보다 커야 합니다: " + pollIntervalMillis);
        }
        if (leaseSeconds <= 0) {
            throw new IllegalArgumentException("mafia.redis.game.timer.lease-seconds는 0보다 커야 합니다: " + leaseSeconds);
        }
        if (batchSize <= 0) {
            throw new IllegalArgumentException("mafia.redis.game.timer.batch-size는 0보다 커야 합니다: " + batchSize);
        }
        if (workers <= 0) {
            throw new IllegalArgumentException("mafia.redis.game.timer.workers는 0보다 커야 합니다: " + workers);
        }
    }

    public Duration pollInterval() {
        return Duration.ofMillis(pollIntervalMillis);
    }

    public Duration lease() {
        return Duration.ofSeconds(leaseSeconds);
    }
}
