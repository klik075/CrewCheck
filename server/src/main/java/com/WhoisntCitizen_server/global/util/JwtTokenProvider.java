package com.WhoisntCitizen_server.global.util;

import io.jsonwebtoken.*;
import io.jsonwebtoken.security.Keys;
import io.jsonwebtoken.security.SecurityException;
import lombok.extern.slf4j.Slf4j;
import org.springframework.beans.factory.annotation.Value;
import org.springframework.stereotype.Component;

import javax.crypto.SecretKey;
import java.nio.charset.StandardCharsets;
import java.util.Date;

@Slf4j
@Component
public class JwtTokenProvider {

	private final SecretKey key;
	private final long validityInmilliseconds;

	public JwtTokenProvider(
			@Value("${jwt.secret}") String secretKey,
			@Value("${jwt.expiration}") long validityInmilliseconds
	) {
		this.key = Keys.hmacShaKeyFor(secretKey.getBytes(StandardCharsets.UTF_8));
		this.validityInmilliseconds = validityInmilliseconds;
	}

	public String createToken(Long memberId, String username) {
		Date now = new Date();
		Date validity = new Date(now.getTime() + validityInmilliseconds);

		return Jwts.builder()
				.subject(String.valueOf(memberId))
				.issuedAt(now)
				.expiration(validity)
				.signWith(key)
				.compact();
	}

	public String getMemberId(String token) {
		Claims claims = parseClaims(token);
		return claims.get("username", String.class);
	}

	public boolean validateToken(String token) {
		try {
			parseClaims(token);
			return true;
		} catch (SecurityException | MalformedJwtException e) {
			log.warn("잘못된 JWT 서명입니다: {}", e.getMessage());
		} catch (ExpiredJwtException e) {
			log.warn("만료된 JWT 토큰입니다: {}", e.getMessage());
		} catch (UnsupportedJwtException e) {
			log.warn("지원되지 않는 JWT 토큰입니다: {}", e.getMessage());
		} catch (IllegalArgumentException e) {
			log.warn("JWT 토큰이 비어있습니다: {}", e.getMessage());
		}
		return false;
	}

	private Claims parseClaims(String token) {
		return Jwts.parser()
				.verifyWith(key)
				.build()
				.parseSignedClaims(token)
				.getPayload();
	}
}
