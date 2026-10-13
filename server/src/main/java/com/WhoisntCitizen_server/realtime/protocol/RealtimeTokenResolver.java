package com.WhoisntCitizen_server.realtime.protocol;

import org.springframework.http.HttpHeaders;
import org.springframework.web.util.UriComponentsBuilder;

import java.net.URI;
import java.util.Optional;

/**
 * WebSocket 연결(handshake) 요청에서 JWT를 꺼낸다. 검증은 하지 않는다. (4-2에서 JwtTokenProvider로 검증)
 *
 * 순서
 *  1) Authorization: Bearer {토큰}  — REST와 같은 방식. Unity 데스크톱·모바일 빌드
 *  2) ?token={토큰}                  — 헤더를 못 넣는 브라우저·WebGL 빌드
 * 헤더가 있으면 쿼리는 보지 않는다. (둘이 달라 헷갈리는 일을 막기 위해 헤더를 우선한다)
 */
public final class RealtimeTokenResolver {

    private static final String BEARER = "Bearer ";

    private RealtimeTokenResolver() {
    }

    public static Optional<String> resolve(HttpHeaders headers, URI uri) {
        String authorization = headers == null ? null : headers.getFirst(HttpHeaders.AUTHORIZATION);
        if (authorization != null && !authorization.isBlank()) {
            if (authorization.regionMatches(true, 0, BEARER, 0, BEARER.length())) {
                return nonBlank(authorization.substring(BEARER.length()));
            }
            return Optional.empty(); // Bearer가 아닌 헤더(Basic 등)는 받지 않는다
        }
        if (uri == null) {
            return Optional.empty();
        }
        String token = UriComponentsBuilder.fromUri(uri).build().getQueryParams()
                .getFirst(RealtimeProtocol.TOKEN_QUERY_PARAM);
        return nonBlank(token);
    }

    private static Optional<String> nonBlank(String value) {
        if (value == null) {
            return Optional.empty();
        }
        String trimmed = value.trim();
        return trimmed.isEmpty() ? Optional.empty() : Optional.of(trimmed);
    }
}
