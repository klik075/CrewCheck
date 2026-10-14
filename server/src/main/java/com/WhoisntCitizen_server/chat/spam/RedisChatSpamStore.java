package com.WhoisntCitizen_server.chat.spam;

import org.springframework.data.redis.core.StringRedisTemplate;
import org.springframework.data.redis.core.script.DefaultRedisScript;

import java.util.List;

/**
 * Redis에 두는 도배 기록 (채팅 메시지와 같은 로비 Redis). 서버가 여러 대여도 같은 기록을 본다.
 *  - chat:v1:spam:{userId}:mute    (String) 채팅 금지 표시. TTL = 남은 금지 시간
 *  - chat:v1:spam:{userId}:repeat  (Hash)   k = 직전 내용(해시), c = 연속 횟수. TTL = repeatWindow (메시지마다 다시 설정)
 * 키 이름의 {userId}는 Redis Cluster 해시 태그라 두 키가 같은 슬롯에 들어간다. (스크립트 한 번에 두 키를 다루기 위해)
 *
 * 확인·기록·금지를 Lua 스크립트 하나로 처리해 같은 사람이 동시에 보내도 횟수가 어긋나지 않는다.
 */
public class RedisChatSpamStore implements ChatSpamStore {

    private static final String KEY_PREFIX = "chat:v1:spam:";

    /**
     * KEYS[1] = mute 키, KEYS[2] = repeat 키
     * ARGV[1] = 내용 해시, ARGV[2] = muteAt, ARGV[3] = 금지 시간(ms), ARGV[4] = repeatWindow(ms)
     * 반환: {-1, 남은 금지 ms} 이미 금지 중 / {횟수, 새 금지 ms 또는 0}
     */
    private static final String SCRIPT = """
            local muteTtl = redis.call('PTTL', KEYS[1])
            if muteTtl > 0 then
              return {-1, muteTtl}
            end
            local count = 1
            if redis.call('HGET', KEYS[2], 'k') == ARGV[1] then
              count = tonumber(redis.call('HGET', KEYS[2], 'c') or '0') + 1
            end
            if count >= tonumber(ARGV[2]) then
              redis.call('DEL', KEYS[2])
              redis.call('SET', KEYS[1], '1', 'PX', ARGV[3])
              return {count, tonumber(ARGV[3])}
            end
            redis.call('HSET', KEYS[2], 'k', ARGV[1], 'c', count)
            redis.call('PEXPIRE', KEYS[2], ARGV[4])
            return {count, 0}
            """;

    @SuppressWarnings("rawtypes")
    private static final DefaultRedisScript<List> RECORD = new DefaultRedisScript<>(SCRIPT, List.class);

    private final StringRedisTemplate redis;

    public RedisChatSpamStore(StringRedisTemplate redis) {
        this.redis = redis;
    }

    static String muteKey(long userId) {
        return KEY_PREFIX + "{" + userId + "}:mute";
    }

    static String repeatKey(long userId) {
        return KEY_PREFIX + "{" + userId + "}:repeat";
    }

    @Override
    public SpamRecord record(long userId, String contentKey, ChatSpamPolicy policy) {
        List<?> result = redis.execute(RECORD, List.of(muteKey(userId), repeatKey(userId)),
                contentKey,
                String.valueOf(policy.muteAt()),
                String.valueOf(policy.muteDuration().toMillis()),
                String.valueOf(policy.repeatWindow().toMillis()));
        if (result == null || result.size() < 2) {
            return SpamRecord.counted(1, 0); // 결과를 못 읽으면 막지 않는다
        }
        long first = toLong(result.get(0));
        long second = toLong(result.get(1));
        if (first < 0) return SpamRecord.muted(second);
        return SpamRecord.counted((int) first, second);
    }

    private static long toLong(Object v) {
        if (v instanceof Number n) return n.longValue();
        return v == null ? 0 : Long.parseLong(v.toString());
    }
}
