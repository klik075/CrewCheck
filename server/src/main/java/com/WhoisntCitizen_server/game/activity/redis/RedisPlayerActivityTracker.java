package com.WhoisntCitizen_server.game.activity.redis;

import com.WhoisntCitizen_server.game.activity.PlayerActivityTracker;
import com.WhoisntCitizen_server.game.repository.redis.GameRedis;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;
import org.springframework.data.redis.core.StringRedisTemplate;
import org.springframework.data.redis.core.script.DefaultRedisScript;
import org.springframework.data.redis.core.script.RedisScript;

import java.time.Duration;
import java.time.Instant;
import java.util.ArrayList;
import java.util.Collection;
import java.util.HashMap;
import java.util.List;
import java.util.Map;
import java.util.Objects;

/**
 * Redis에 접속 기록을 두는 PlayerActivityTracker. 게임 그룹 Redis(GameRedis)를 쓴다.
 * 서버가 여러 대여도 모든 서버가 같은 기록을 본다. (서버 A가 받은 요청을 서버 B의 미접속 검사가 본다)
 *
 * 키 구조
 *  - game:v1:activity:{gameId}  (Hash)  field = playerId, value = 마지막 요청 시각(epoch ms)
 *    예) game:v1:activity:abc → { "1": "1791631095000", "2": "1791631097250" }
 *
 * 1) 기록 (touch, markAllSeen → Lua 스크립트 TOUCH)
 *    기존 값보다 늦을 때만 HSET 한다. 읽기와 쓰기가 한 스크립트라 Redis 안에서 한 번에 실행되므로,
 *    서버 여러 대가 동시에 기록해도 더 늦은 시각이 남는다. (계약 3번)
 *    HGET → 비교 → HSET을 자바에서 나눠 하면, 그 사이에 다른 서버가 쓴 최신 값을 덮어쓸 수 있다.
 *    markAllSeen은 참가자 전원을 스크립트 한 번으로 기록한다. (왕복 1번)
 *
 * 2) 조회 (lastSeen): HGETALL 한 번. 새 Map으로 바꿔 돌려주므로 복사본이다. (계약 6번)
 *
 * 3) 정리 (clear): DEL 한 번.
 *
 * TTL: 기록할 때마다 키 전체의 TTL을 다시 건다. 정리(clear)가 돌지 못했을 때(서버가 죽는 등) 키가 영원히 남지 않게 하는
 * 안전장치다. 게임 상태 키의 activeTtl(기본 6시간)과 같은 값을 쓴다. 요청이 계속 오는 동안은 사라지지 않는다.
 *
 * 시각은 밀리초까지만 저장한다. 그보다 작은 단위는 버려진다. (연결 끊김 판정은 초 단위라 문제 없다)
 */
public class RedisPlayerActivityTracker implements PlayerActivityTracker {

    static final String KEY_PREFIX = "game:v1:activity:";
    /** 기본 TTL. 게임 상태 키의 activeTtlSeconds 기본값(21600초)과 같다 */
    public static final Duration DEFAULT_TTL = Duration.ofHours(6);

    private static final Logger log = LoggerFactory.getLogger(RedisPlayerActivityTracker.class);

    /**
     * 더 늦은 시각만 기록하고 TTL을 다시 건다.
     * KEYS[1] = activity 키, ARGV[1] = TTL(ms), ARGV[2..] = playerId, 시각(ms), playerId, 시각(ms), ...
     * 바꾼 개수를 돌려준다. (테스트·점검용)
     */
    private static final RedisScript<Long> TOUCH = new DefaultRedisScript<>(
            "local changed = 0\n"
                    + "for i = 2, #ARGV, 2 do\n"
                    + "  local current = redis.call('HGET', KEYS[1], ARGV[i])\n"
                    + "  if (not current) or tonumber(ARGV[i + 1]) > tonumber(current) then\n"
                    + "    redis.call('HSET', KEYS[1], ARGV[i], ARGV[i + 1])\n"
                    + "    changed = changed + 1\n"
                    + "  end\n"
                    + "end\n"
                    + "redis.call('PEXPIRE', KEYS[1], ARGV[1])\n"
                    + "return changed",
            Long.class);

    private final StringRedisTemplate redis;
    private final Duration ttl;

    public RedisPlayerActivityTracker(GameRedis gameRedis) {
        this(gameRedis, DEFAULT_TTL);
    }

    public RedisPlayerActivityTracker(GameRedis gameRedis, Duration ttl) {
        this.redis = Objects.requireNonNull(gameRedis, "gameRedis").template();
        this.ttl = Objects.requireNonNull(ttl, "ttl");
        if (ttl.isNegative() || ttl.isZero()) {
            throw new IllegalArgumentException("ttl은 0보다 커야 합니다: " + ttl);
        }
    }

    static String key(String gameId) {
        return KEY_PREFIX + gameId;
    }

    @Override
    public void markAllSeen(String gameId, Collection<Long> playerIds, Instant now) {
        if (gameId == null || playerIds == null || now == null) {
            return;
        }
        List<String> args = new ArrayList<>();
        args.add(Long.toString(ttl.toMillis()));
        String millis = Long.toString(now.toEpochMilli());
        for (Long playerId : playerIds) {
            if (playerId != null) {
                args.add(playerId.toString());
                args.add(millis);
            }
        }
        if (args.size() == 1) {
            return; // 기록할 참가자가 없으면 키를 만들지 않는다
        }
        redis.execute(TOUCH, List.of(key(gameId)), args.toArray());
    }

    @Override
    public void touch(String gameId, Long playerId, Instant now) {
        if (gameId == null || playerId == null || now == null) {
            return;
        }
        redis.execute(TOUCH, List.of(key(gameId)),
                Long.toString(ttl.toMillis()), playerId.toString(), Long.toString(now.toEpochMilli()));
    }

    @Override
    public Map<Long, Instant> lastSeen(String gameId) {
        if (gameId == null) {
            return Map.of();
        }
        Map<Object, Object> raw = redis.opsForHash().entries(key(gameId));
        if (raw.isEmpty()) {
            return Map.of();
        }
        Map<Long, Instant> result = new HashMap<>();
        raw.forEach((field, value) -> {
            try {
                result.put(Long.parseLong(field.toString()), Instant.ofEpochMilli(Long.parseLong(value.toString())));
            } catch (NumberFormatException e) {
                // 누가 손으로 잘못 넣은 값 하나 때문에 미접속 검사 전체가 멈추지 않게 건너뛴다
                log.warn("접속 기록 형식이 잘못되어 건너뜁니다. gameId={}, field={}, value={}", gameId, field, value);
            }
        });
        return Map.copyOf(result);
    }

    @Override
    public void clear(String gameId) {
        if (gameId != null) {
            redis.delete(key(gameId));
        }
    }
}
