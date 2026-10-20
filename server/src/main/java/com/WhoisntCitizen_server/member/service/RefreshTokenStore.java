package com.WhoisntCitizen_server.member.service;

import org.springframework.beans.factory.annotation.Value;
import org.springframework.data.redis.core.StringRedisTemplate;
import org.springframework.data.redis.core.script.DefaultRedisScript;
import org.springframework.stereotype.Component;

import java.nio.charset.StandardCharsets;
import java.security.MessageDigest;
import java.security.SecureRandom;
import java.time.Duration;
import java.util.Base64;
import java.util.List;
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

	/**
	 * 저장된 값이 oldToken과 같을 때만 newToken으로 바꾼다. 비교와 교체를 Redis 안에서 한 번에 처리한다(원자적).
	 * 자바에서 matches() 후 issue()를 따로 부르면 그 사이에 같은 토큰으로 온 요청이 함께 통과할 수 있다.
	 * KEYS[1]=키, ARGV[1]=기존 토큰, ARGV[2]=새 토큰, ARGV[3]=TTL(ms). 바꿨으면 1, 아니면 0.
	 */
	private static final DefaultRedisScript<Long> ROTATE_SCRIPT = new DefaultRedisScript<>(
			"if redis.call('GET', KEYS[1]) == ARGV[1] then "
					+ "redis.call('SET', KEYS[1], ARGV[2], 'PX', ARGV[3]) return 1 end return 0",
			Long.class);

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
		String token = newToken(memberId);
		redis.opsForValue().set(key(memberId), token, ttl);
		return token;
	}

	/**
	 * 저장된 토큰이 oldToken과 같을 때만 새 토큰으로 교체하고 새 토큰을 돌려준다. 다르면(이미 다른 요청이 썼거나 만료) 비어 있다.
	 * 같은 토큰으로 동시에 여러 요청이 와도 하나만 성공한다.
	 */
	public Optional<String> rotate(Long memberId, String oldToken) {
		String token = newToken(memberId);
		Long replaced = redis.execute(ROTATE_SCRIPT, List.of(key(memberId)), oldToken, token, String.valueOf(ttl.toMillis()));
		return Long.valueOf(1L).equals(replaced) ? Optional.of(token) : Optional.empty();
	}

	private String newToken(Long memberId) {
		byte[] bytes = new byte[RANDOM_BYTES];
		random.nextBytes(bytes);
		return memberId + "." + Base64.getUrlEncoder().withoutPadding().encodeToString(bytes);
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
