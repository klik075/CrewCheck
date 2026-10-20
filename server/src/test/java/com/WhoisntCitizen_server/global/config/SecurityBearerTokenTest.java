package com.WhoisntCitizen_server.global.config;

import com.WhoisntCitizen_server.global.util.JwtTokenProvider;
import org.junit.jupiter.api.Test;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.boot.test.context.SpringBootTest;
import org.springframework.boot.webmvc.test.autoconfigure.AutoConfigureMockMvc;
import org.springframework.http.HttpHeaders;
import org.springframework.test.web.servlet.MockMvc;

import static org.hamcrest.Matchers.not;
import static org.springframework.test.web.servlet.request.MockMvcRequestBuilders.get;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.status;

/**
 * SecurityConfig.bearerTokenResolver: /ws/** 만 Security가 토큰을 읽지 않고, REST는 지금처럼 검증한다.
 * (WebSocket 쪽 동작은 RealtimeWebSocketConnectionTest에서 실제 연결로 확인한다)
 */
@SpringBootTest
@AutoConfigureMockMvc
class SecurityBearerTokenTest {

    @Autowired
    MockMvc mvc;

    @Autowired
    JwtTokenProvider jwtTokenProvider;

    @Test
    void REST는_잘못된_Bearer_토큰이면_지금처럼_401이다() throws Exception {
        mvc.perform(get("/api/v1/rooms").header(HttpHeaders.AUTHORIZATION, "Bearer not-a-jwt"))
                .andExpect(status().isUnauthorized());
    }

    @Test
    void REST는_올바른_Bearer_토큰이면_인증을_통과한다() throws Exception {
        String token = jwtTokenProvider.createToken(15L, "tester");
        mvc.perform(get("/api/v1/rooms").header(HttpHeaders.AUTHORIZATION, "Bearer " + token))
                .andExpect(status().is(not(401)));
    }

    @Test
    void ws_경로는_잘못된_Bearer_토큰이어도_Security가_401로_막지_않는다() throws Exception {
        // MockMvc는 WebSocket 업그레이드를 하지 않으므로 응답 코드는 401만 아니면 된다.
        // (401이면 Security가 먼저 막은 것 → 클라이언트가 4401을 받을 수 없다)
        mvc.perform(get("/ws/v1").header(HttpHeaders.AUTHORIZATION, "Bearer not-a-jwt"))
                .andExpect(status().is(not(401)));
    }
}
