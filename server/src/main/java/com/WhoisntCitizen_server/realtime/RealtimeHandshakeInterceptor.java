package com.WhoisntCitizen_server.realtime;

import com.WhoisntCitizen_server.realtime.protocol.RealtimeTokenResolver;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;
import org.springframework.http.HttpStatus;
import org.springframework.http.server.ServerHttpRequest;
import org.springframework.http.server.ServerHttpResponse;
import org.springframework.security.oauth2.jwt.Jwt;
import org.springframework.security.oauth2.jwt.JwtDecoder;
import org.springframework.security.oauth2.jwt.JwtException;
import org.springframework.web.socket.WebSocketHandler;
import org.springframework.web.socket.WebSocketSession;
import org.springframework.web.socket.server.HandshakeInterceptor;

import java.time.Instant;
import java.util.Map;
import java.util.Objects;
import java.util.Optional;

/**
 * WebSocket 연결(handshake) 때 JWT를 확인한다. 통과해야 WebSocket이 열린다.
 *
 *  - 토큰 위치: Authorization: Bearer 헤더 → 없으면 ?token= (RealtimeTokenResolver)
 *  - 검증: REST와 같은 JwtDecoder(SecurityConfig, HS256, 만료 확인)
 *  - 실패: HTTP 401로 거절. WebSocket이 열리지 않으므로 클라이언트는 연결 실패로 받는다
 *  - 성공: 사용자 id(토큰 sub)와 만료 시각을 세션 속성에 넣어 둔다. 이후 핸들러는 토큰을 다시 보지 않는다
 *
 * 연결 주소에 토큰이 들어 있을 수 있으므로 주소(URI)는 로그에 남기지 않는다.
 */
public class RealtimeHandshakeInterceptor implements HandshakeInterceptor {

    /** 세션 속성: 인증된 사용자 id (Long) */
    public static final String ATTR_USER_ID = "realtime.userId";
    /** 세션 속성: 토큰 만료 시각 (Instant, 없을 수 있음). 연결 중 만료 처리(CLOSE_TOKEN_EXPIRED)에 쓴다 */
    public static final String ATTR_TOKEN_EXPIRES_AT = "realtime.tokenExpiresAt";

    private static final Logger log = LoggerFactory.getLogger(RealtimeHandshakeInterceptor.class);

    private final JwtDecoder jwtDecoder;

    public RealtimeHandshakeInterceptor(JwtDecoder jwtDecoder) {
        this.jwtDecoder = Objects.requireNonNull(jwtDecoder, "jwtDecoder");
    }

    @Override
    public boolean beforeHandshake(ServerHttpRequest request, ServerHttpResponse response,
                                   WebSocketHandler wsHandler, Map<String, Object> attributes) {
        Optional<String> token = RealtimeTokenResolver.resolve(request.getHeaders(), request.getURI());
        if (token.isEmpty()) {
            return reject(request, response, "토큰 없음");
        }
        Jwt jwt;
        try {
            jwt = jwtDecoder.decode(token.get());
        } catch (JwtException e) {
            return reject(request, response, "토큰 검증 실패: " + e.getClass().getSimpleName());
        }
        Long userId = parseUserId(jwt.getSubject());
        if (userId == null) {
            return reject(request, response, "토큰에 사용자 id(sub)가 없음");
        }
        attributes.put(ATTR_USER_ID, userId);
        Instant expiresAt = jwt.getExpiresAt();
        if (expiresAt != null) {
            attributes.put(ATTR_TOKEN_EXPIRES_AT, expiresAt);
        }
        return true;
    }

    @Override
    public void afterHandshake(ServerHttpRequest request, ServerHttpResponse response,
                               WebSocketHandler wsHandler, Exception exception) {
        // 할 일 없음
    }

    /** 연결된 세션의 사용자 id. handshake를 통과한 세션에만 있다 */
    public static long userId(WebSocketSession session) {
        Object value = session.getAttributes().get(ATTR_USER_ID);
        if (value instanceof Long id) {
            return id;
        }
        throw new IllegalStateException("인증되지 않은 WebSocket 세션입니다: " + session.getId());
    }

    private static Long parseUserId(String subject) {
        if (subject == null) {
            return null;
        }
        try {
            return Long.valueOf(subject);
        } catch (NumberFormatException e) {
            return null;
        }
    }

    private static boolean reject(ServerHttpRequest request, ServerHttpResponse response, String reason) {
        response.setStatusCode(HttpStatus.UNAUTHORIZED);
        log.info("[ws] 연결 거부: {} (remote={})", reason, request.getRemoteAddress());
        return false;
    }
}
