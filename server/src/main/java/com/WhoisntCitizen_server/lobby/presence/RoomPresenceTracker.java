package com.WhoisntCitizen_server.lobby.presence;

import com.WhoisntCitizen_server.lobby.config.LobbyProperties;
import org.springframework.data.redis.core.StringRedisTemplate;
import org.springframework.data.redis.core.script.DefaultRedisScript;
import org.springframework.data.redis.core.script.RedisScript;
import org.springframework.stereotype.Component;

import java.time.Instant;
import java.util.ArrayList;
import java.util.Collection;
import java.util.HashMap;
import java.util.List;
import java.util.Map;

/**
 * 대기실 접속 기록: 방 참가자별 마지막 활동 시각. 대기실 연결 끊김 판정(RoomPresenceService)에 쓴다.
 * 게임 쪽 PlayerActivityTracker와 같은 역할을 대기실(WAITING 방)에서 한다.
 *
 * ---------------------------------------------------------------------------
 * 왜 Redis 구현 하나만 있나 (메모리 구현 없음)
 * ---------------------------------------------------------------------------
 *   잠금·타이머·게임 접속 기록은 서버 모드(mafia.server.mode)에 따라 메모리/Redis 중에서 고르지만, 이 기록은 Redis 고정이다.
 *   - 방 데이터(room:{id}, rooms)가 single 모드에서도 이미 로비 Redis에 있다. 새로운 의존성이 생기지 않는다.
 *   - 모든 서버가 같은 기록을 보므로 서버가 1대든 여러 대든 같은 코드로 동작한다.
 *     (메모리에 두면 서버 A가 받은 방 조회를 서버 B의 검사가 못 봐서 멀쩡한 사람을 뺀다)
 *   - 방 조회도 원래 1초마다 Redis에서 방을 읽고 있어서, 짧은 스크립트 한 번이 더해지는 정도다.
 *
 * ---------------------------------------------------------------------------
 * 키 구조 (로비 Redis = spring.data.redis.*, 기본 StringRedisTemplate)
 * ---------------------------------------------------------------------------
 *   lobby:v1:presence:{roomId}  (Hash)  field = userId, value = 마지막 활동 시각(epoch ms)
 *   예) lobby:v1:presence:5 → { "10": "1791631095000", "20": "1791631097250" }
 *
 *   TTL: 기록할 때마다 키 전체의 TTL(presenceTtlSeconds, 기본 1시간)을 다시 건다.
 *        사람이 있는 방은 1초마다 기록되고, 게임 중인 방은 검사가 5초마다 기록하므로 만료되지 않는다.
 *        방이 삭제될 때 키를 지우지만(RoomPresenceService.onRoomDeleted), 그걸 놓쳐도(서버가 죽는 등) 결국 사라지게 하는 안전장치다.
 *
 * ---------------------------------------------------------------------------
 * 왜 Lua 스크립트를 쓰나
 * ---------------------------------------------------------------------------
 *   "지금 값 읽기 → 비교 → 쓰기"를 자바에서 명령 여러 개로 나누면, 그 사이에 다른 서버가 쓴 최신 값을 옛날 값으로 덮어쓸 수 있다.
 *   Lua 스크립트는 Redis 안에서 한 번에(중간에 다른 명령이 끼어들지 않고) 실행되므로 이런 일이 생기지 않는다.
 *   스크립트가 도는 동안 Redis 전체가 기다리므로, 스크립트는 짧게 유지한다. (지금은 참가자 수(최대 12)만큼만 반복)
 */
@Component
public class RoomPresenceTracker {

    static final String KEY_PREFIX = "lobby:v1:presence:";

    /**
     * 더 늦은 시각일 때만 기록하고 TTL을 다시 건다. (게임 쪽 RedisPlayerActivityTracker.TOUCH와 같은 규칙)
     * KEYS[1] = 기록 키, ARGV[1] = TTL(ms), ARGV[2..] = userId, 시각(ms), userId, 시각(ms), ...
     *
     * "더 늦을 때만"인 이유
     *   - 서버 여러 대가 동시에 기록해도 항상 가장 늦은 시각이 남는다.
     *   - 게임 중 검사가 미리 넣어 둔 미래 시각(결과 화면 유예)을 방 조회가 지금 시각으로 끌어내리지 않는다.
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

    /**
     * 기록이 없는 참가자에게만 시각을 넣고(HSETNX) TTL을 다시 건다.
     * 서버 재시작·키 만료 등으로 기록이 없는 사람을 바로 빼지 않고 "지금부터" 세기 위해 쓴다.
     * 인자 구조는 TOUCH와 같다.
     */
    private static final RedisScript<Long> INIT_MISSING = new DefaultRedisScript<>(
            "local added = 0\n"
                    + "for i = 2, #ARGV, 2 do\n"
                    + "  added = added + redis.call('HSETNX', KEYS[1], ARGV[i], ARGV[i + 1])\n"
                    + "end\n"
                    + "redis.call('PEXPIRE', KEYS[1], ARGV[1])\n"
                    + "return added",
            Long.class);

    private final StringRedisTemplate redis;
    private final LobbyProperties props;

    public RoomPresenceTracker(StringRedisTemplate redis, LobbyProperties props) {
        this.redis = redis;
        this.props = props;
    }

    static String key(Long roomId) {
        return KEY_PREFIX + roomId;
    }

    /** 한 사람의 활동을 기록한다. (방 조회, 방 생성·입장) */
    public void touch(Long roomId, Long userId, Instant at) {
        if (roomId == null || userId == null || at == null) return;
        run(TOUCH, roomId, List.of(userId), at);
    }

    /**
     * 여러 명을 같은 시각으로 기록한다. (더 늦을 때만)
     * 게임 중인 방 검사에서 "지금 + 결과 조회 시간"처럼 미래 시각을 넣어, 게임이 끝난 뒤 결과 화면에 있는 동안 유예를 준다.
     */
    public void markAll(Long roomId, Collection<Long> userIds, Instant at) {
        run(TOUCH, roomId, userIds, at);
    }

    /** 기록이 없는 사람에게만 시각을 넣는다. 이미 기록이 있는 사람은 그대로 둔다. */
    public void initMissing(Long roomId, Collection<Long> userIds, Instant at) {
        run(INIT_MISSING, roomId, userIds, at);
    }

    /** 방의 참가자별 마지막 활동 시각. 키가 없으면 빈 Map. (HGETALL 한 번, 돌려주는 Map은 복사본) */
    public Map<Long, Instant> lastSeen(Long roomId) {
        Map<Object, Object> raw = redis.opsForHash().entries(key(roomId));
        Map<Long, Instant> result = new HashMap<>();
        raw.forEach((field, value) -> {
            try {
                result.put(Long.valueOf(field.toString()), Instant.ofEpochMilli(Long.parseLong(value.toString())));
            } catch (NumberFormatException ignored) {
                // 잘못 들어간 값은 무시한다. (다음 검사에서 방에 없는 사람으로 보고 지워진다)
            }
        });
        return result;
    }

    /** 일부 참가자의 기록을 지운다. (방에서 나간 사람, 퇴출된 사람) */
    public void remove(Long roomId, Collection<Long> userIds) {
        if (userIds == null || userIds.isEmpty()) return;
        redis.opsForHash().delete(key(roomId), userIds.stream().map(String::valueOf).toArray());
    }

    /** 방의 기록 키를 통째로 지운다. (방 삭제) */
    public void delete(Long roomId) {
        redis.delete(key(roomId));
    }

    /** TOUCH / INIT_MISSING 공통 실행: ARGV = [TTL(ms), userId, 시각(ms), userId, 시각(ms), ...] */
    private void run(RedisScript<Long> script, Long roomId, Collection<Long> userIds, Instant at) {
        if (roomId == null || userIds == null || at == null) return;
        List<String> args = new ArrayList<>();
        args.add(Long.toString(props.presenceTtlSeconds() * 1000L));
        String millis = Long.toString(at.toEpochMilli());
        for (Long userId : userIds) {
            if (userId != null) {
                args.add(userId.toString());
                args.add(millis);
            }
        }
        if (args.size() == 1) return; // 기록할 사람이 없으면 키를 만들지 않는다
        redis.execute(script, List.of(key(roomId)), args.toArray());
    }
}
