package com.WhoisntCitizen_server.realtime;

import com.WhoisntCitizen_server.realtime.protocol.RealtimeTokenResolver;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;
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
 *  - 실패(토큰 없음·검증 실패·sub 없음): HTTP 401로 거절하지 않고 연결은 열어 준다. 대신 세션 속성에 실패 이유(ATTR_AUTH_ERROR)를
 *    넣어 두고, RealtimeWebSocketHandler가 연결 직후 close 4401(CLOSE_TOKEN_EXPIRED)로 바로 닫는다.
 *    왜: HTTP 401은 Unity(Mono ClientWebSocket)와 브라우저(WebGL) 모두 "연결 실패"로만 알려 줘서 클라이언트가 토큰 문제인지 모르고
 *    재연결을 반복한다. close code 4401은 두 플랫폼 모두 그대로 전달되므로 "다시 로그인"으로 처리할 수 있다.
 *    인증 실패 연결은 연결 목록·접속 상태에 등록되지 않고, 메시지를 하나도 받지 못한 채 닫힌다.
 *  - 성공: 사용자 id(토큰 sub)와 만료 시각을 세션 속성에 넣어 둔다. 이후 핸들러는 토큰을 다시 보지 않는다
 *
 * 연결 주소에 토큰이 들어 있을 수 있으므로 주소(URI)는 로그에 남기지 않는다.
 */
public class RealtimeHandshakeInterceptor implements HandshakeInterceptor {

    /** 세션 속성: 인증된 사용자 id (Long) */
    public static final String ATTR_USER_ID = "realtime.userId";
    /** 세션 속성: 토큰 만료 시각 (Instant, 없을 수 있음). 연결 중 만료 처리(CLOSE_TOKEN_EXPIRED)에 쓴다 */
    public static final String ATTR_TOKEN_EXPIRES_AT = "realtime.tokenExpiresAt";
    /** 세션 속성: 인증 실패 이유 (String). 있으면 핸들러가 연결 직후 4401로 닫는다. 성공한 연결에는 없다 */
    public static final String ATTR_AUTH_ERROR = "realtime.authError";

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
            return markUnauthorized(request, attributes, "토큰 없음");
        }
        Jwt jwt;
        try {
            jwt = jwtDecoder.decode(token.get());
        } catch (JwtException e) {
            return markUnauthorized(request, attributes, "토큰 검증 실패: " + e.getClass().getSimpleName());
        }
        Long userId = parseUserId(jwt.getSubject());
        if (userId == null) {
            return markUnauthorized(request, attributes, "토큰에 사용자 id(sub)가 없음");
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

    /**
     * 인증 실패를 표시하고 handshake는 통과시킨다. (연결 직후 핸들러가 4401로 닫는다)
     * 사용자 id는 넣지 않는다. 그래서 핸들러가 이 연결을 연결 목록·접속 상태에 등록할 수 없다.
     */
    private static boolean markUnauthorized(ServerHttpRequest request, Map<String, Object> attributes, String reason) {
        attributes.put(ATTR_AUTH_ERROR, reason);
        log.info("[ws] 인증 실패 → 연결 후 4401로 닫음: {} (remote={})", reason, request.getRemoteAddress());
        return true;
    }
}
