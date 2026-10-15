package com.WhoisntCitizen_server.member.service;

import org.springframework.beans.factory.annotation.Value;
import org.springframework.data.redis.core.StringRedisTemplate;
import org.springframework.stereotype.Component;

import java.nio.charset.StandardCharsets;
import java.security.MessageDigest;
import java.security.SecureRandom;
import java.time.Duration;
import java.util.Base64;
import java.util.Optional;

/**
 * 리프레시 토큰 저장소. Redis에 refresh_token:{memberId} = 토큰값 (TTL 7일)으로 회원당 하나만 둔다.
 *
 * 토큰 모양은 "{memberId}.{무작위 값}"이다. JWT가 아니라서 액세스 토큰(JWT)으로 잘못 통과될 일이 없고,
 * 앞부분의 memberId로 Redis 키를 바로 찾을 수 있다.
 * 같은 회원이 다시 로그인하면 값이 덮어써져 이전 기기의 리프레시 토큰은 무효가 된다.
 */
@Component
public class RefreshTokenStore {

	private static final String KEY_PREFIX = "refresh_token:";
	private static final int RANDOM_BYTES = 32;

	private final StringRedisTemplate redis;
	private final Duration ttl;
	private final SecureRandom random = new SecureRandom();

	public RefreshTokenStore(
			StringRedisTemplate redis,
			@Value("${jwt.refresh-expiration:604800000}") long refreshExpirationMillis
	) {
		this.redis = redis;
		this.ttl = Duration.ofMillis(refreshExpirationMillis);
	}

	/** 새 리프레시 토큰을 만들어 저장하고(기존 값은 덮어씀) 돌려준다. */
	public String issue(Long memberId) {
		byte[] bytes = new byte[RANDOM_BYTES];
		random.nextBytes(bytes);
		String token = memberId + "." + Base64.getUrlEncoder().withoutPadding().encodeToString(bytes);
		redis.opsForValue().set(key(memberId), token, ttl);
		return token;
	}

	/** 토큰 앞부분의 memberId. 형식이 맞지 않으면 비어 있다. */
	public Optional<Long> parseMemberId(String token) {
		if (token == null) return Optional.empty();
		int dot = token.indexOf('.');
		if (dot <= 0) return Optional.empty();
		try {
			return Optional.of(Long.parseLong(token.substring(0, dot)));
		} catch (NumberFormatException e) {
			return Optional.empty();
		}
	}

	/** 저장된 토큰과 같은지 (시간 일정 비교). 저장된 값이 없으면(만료·폐기) false. */
	public boolean matches(Long memberId, String token) {
		String stored = redis.opsForValue().get(key(memberId));
		if (stored == null || token == null) return false;
		return MessageDigest.isEqual(stored.getBytes(StandardCharsets.UTF_8), token.getBytes(StandardCharsets.UTF_8));
	}

	/** 회원의 리프레시 토큰을 폐기한다. */
	public void revoke(Long memberId) {
		redis.delete(key(memberId));
	}

	private static String key(Long memberId) {
		return KEY_PREFIX + memberId;
	}
}
