package com.WhoisntCitizen_server.user.service;

import lombok.RequiredArgsConstructor;
import lombok.extern.slf4j.Slf4j;
import org.springframework.data.redis.core.StringRedisTemplate;
import org.springframework.stereotype.Service;

import java.time.Duration;
import java.util.ArrayList;
import java.util.Collection;
import java.util.HashMap;
import java.util.List;
import java.util.Map;

/**
 * 유저 접속 상태 저장소 (Redis). 키 = user:presence:{userId}, 값 = UserPresenceStatus 이름, TTL = 60초.
 *
 * 쓰는 쪽: 실시간 연결(WebSocket)이다. (realtime.UserPresenceRecorder)
 *   - 연결되면 ONLINE, 10초마다 오는 ping으로 TTL을 다시 걸고, 방/게임에 들어가면 IN_ROOM/IN_GAME
 *   - 연결이 끊기면 바로 지운다. 서버가 죽어서 끊김을 못 받아도 TTL(60초)이 지나면 저절로 OFFLINE이 된다.
 *   - 서버가 정상 종료하면서 끊은 연결은 지우지 않고 TTL만 짧게(기본 10초) 줄인다. (expireSoon, 배포 중 깜빡임 방지)
 * 읽는 쪽: 친구 목록(FriendService).
 *
 * userId는 User.id(프로필 id)다. JWT의 sub(memberId)가 아니다.
 */
@Slf4j
@Service
@RequiredArgsConstructor
public class UserPresenceService {

	private final StringRedisTemplate redisTemplate;

	private static final String PRESENCE_KEY_PREFIX = "user:presence:";
	private static final Duration PRESENCE_TTL = Duration.ofSeconds(60); // ping(10초)을 몇 번 놓쳐도 버티는 안전장치

	/** 상태를 기록하고 TTL을 60초로 다시 건다. */
	public void setStatus(Long userId, UserPresenceStatus status) {
		if (status == null || status == UserPresenceStatus.OFFLINE) {
			removePresence(userId);
			return;
		}
		redisTemplate.opsForValue().set(key(userId), status.name(), PRESENCE_TTL);
	}

	/** 유저 한 명의 현재 상태. 키가 없으면 OFFLINE. */
	public UserPresenceStatus getStatus(Long userId) {
		return UserPresenceStatus.parse(redisTemplate.opsForValue().get(key(userId)));
	}

	/** 여러 명의 상태를 Redis 왕복 한 번(MGET)으로 읽는다. 친구 목록용. */
	public Map<Long, UserPresenceStatus> getStatuses(Collection<Long> userIds) {
		Map<Long, UserPresenceStatus> result = new HashMap<>();
		if (userIds == null || userIds.isEmpty()) return result;

		List<Long> ids = new ArrayList<>(userIds);
		List<String> values = redisTemplate.opsForValue().multiGet(ids.stream().map(UserPresenceService::key).toList());
		for (int i = 0; i < ids.size(); i++) {
			result.put(ids.get(i), UserPresenceStatus.parse(values == null ? null : values.get(i)));
		}
		return result;
	}

	/** 특정 유저가 현재 온라인인지 확인 (ONLINE, IN_ROOM, IN_GAME 모두 온라인) */
	public boolean isOnline(Long userId) {
		return getStatus(userId).isOnline();
	}

	/**
	 * 지금 상태를 grace 동안만 남기고 그 뒤 저절로 오프라인이 되게 한다. (멀티서버 3단계)
	 * 서버가 꺼지면서 연결을 닫았을 때 쓴다. 곧 다른 서버로 다시 연결하면 setStatus가 TTL을 60초로 다시 건다.
	 * 키가 없으면(이미 오프라인) 아무 일도 하지 않는다.
	 */
	public void expireSoon(Long userId, Duration grace) {
		redisTemplate.expire(key(userId), grace);
	}

	/** 연결이 모두 끊겼을 때 즉시 오프라인 처리 */
	public void removePresence(Long userId) {
		redisTemplate.delete(key(userId));
	}

	private static String key(Long userId) {
		return PRESENCE_KEY_PREFIX + userId;
	}
}
