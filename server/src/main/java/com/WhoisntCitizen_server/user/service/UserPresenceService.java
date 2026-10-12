package com.WhoisntCitizen_server.user.service;

import lombok.RequiredArgsConstructor;
import lombok.extern.slf4j.Slf4j;
import org.springframework.data.redis.core.StringRedisTemplate;
import org.springframework.stereotype.Service;

import java.time.Duration;

@Slf4j
@Service
@RequiredArgsConstructor
public class UserPresenceService {

	private final StringRedisTemplate redisTemplate;

	private static final String PRESENCE_KEY_PREFIX = "user:presence:";
	private static final Duration PRESENCE_TTL = Duration.ofSeconds(60); // 60초 TTL

	/**
	 * 유저의 생존 신호(Heartbeat) 갱신
	 */
	public void updatePresence(Long userId) {
		String key = PRESENCE_KEY_PREFIX + userId;
		// 키 생성 및 TTL 60초 재설정
		redisTemplate.opsForValue().set(key, "ONLINE", PRESENCE_TTL);
	}

	/**
	 * 특정 유저가 현재 온라인인지 확인
	 */
	public boolean isOnline(Long userId) {
		String key = PRESENCE_KEY_PREFIX + userId;
		Boolean exists = redisTemplate.hasKey(key);
		return Boolean.TRUE.equals(exists);
	}

	/**
	 * 유저 명시적 로그아웃 시 즉시 오프라인 처리 (선택 사항)
	 */
	public void removePresence(Long userId) {
		String key = PRESENCE_KEY_PREFIX + userId;
		redisTemplate.delete(key);
	}
}