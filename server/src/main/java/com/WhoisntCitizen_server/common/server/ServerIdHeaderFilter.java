package com.WhoisntCitizen_server.common.server;

import jakarta.servlet.FilterChain;
import jakarta.servlet.ServletException;
import jakarta.servlet.http.HttpServletRequest;
import jakarta.servlet.http.HttpServletResponse;
import org.springframework.core.Ordered;
import org.springframework.web.filter.OncePerRequestFilter;

import java.io.IOException;
import java.util.Objects;

/**
 * 모든 HTTP 응답에 X-Server-Id 헤더를 붙인다. (멀티서버 2단계)
 *
 * 로드 밸런서(nginx) 뒤에 서버가 여러 대 있으면, 요청 하나가 어느 서버로 갔는지 밖에서는 알 수 없다.
 * 이 헤더로 Postman·브라우저 개발자 도구에서 바로 확인한다. (7·8단계에서 분산이 되는지 볼 때 쓴다)
 * WebSocket 연결 요청(/ws/v1 핸드셰이크)도 HTTP라 같은 헤더가 붙는다.
 *
 * 응답 본문보다 먼저 헤더를 넣어야 하므로 체인을 부르기 전에 넣는다. (본문이 쓰이기 시작하면 헤더를 더 붙일 수 없다)
 */
public class ServerIdHeaderFilter extends OncePerRequestFilter implements Ordered {

    public static final String HEADER = "X-Server-Id";

    private final ServerIdentity identity;

    public ServerIdHeaderFilter(ServerIdentity identity) {
        this.identity = Objects.requireNonNull(identity, "identity");
    }

    /** 가장 먼저 실행한다. Spring Security 필터(보안 체인)가 요청을 일찍 끝내도 헤더가 이미 붙어 있게 */
    @Override
    public int getOrder() {
        return Ordered.HIGHEST_PRECEDENCE;
    }

    @Override
    protected void doFilterInternal(HttpServletRequest request, HttpServletResponse response, FilterChain chain)
            throws ServletException, IOException {
        response.setHeader(HEADER, identity.id());
        chain.doFilter(request, response);
    }
}
