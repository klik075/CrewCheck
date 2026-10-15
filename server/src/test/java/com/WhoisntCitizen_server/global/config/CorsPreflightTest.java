package com.WhoisntCitizen_server.global.config;

import org.junit.jupiter.api.Test;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.boot.test.context.SpringBootTest;
import org.springframework.boot.webmvc.test.autoconfigure.AutoConfigureMockMvc;
import org.springframework.http.HttpHeaders;
import org.springframework.test.web.servlet.MockMvc;

import static org.hamcrest.Matchers.containsString;
import static org.hamcrest.Matchers.containsStringIgnoringCase;
import static org.springframework.test.web.servlet.request.MockMvcRequestBuilders.get;
import static org.springframework.test.web.servlet.request.MockMvcRequestBuilders.options;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.header;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.status;

/**
 * W-3-0: 브라우저(WebGL 빌드)에서 REST를 부를 수 있는지. 실제 보안 설정(SecurityConfig)과 CORS 설정(WebConfig)으로 확인한다.
 *
 * 브라우저는 다른 주소(출처)의 서버에 Authorization 헤더를 붙여 요청하기 전에, 먼저 토큰 없이
 * "이 요청 보내도 돼?"를 묻는 사전 요청(OPTIONS)을 보낸다. 이게 통과해야 실제 요청을 보낸다.
 *  - 사전 요청은 토큰이 없어도 인증 검사(authenticated) 전에 200으로 통과해야 한다
 *  - 실제 요청이 401로 끝나도 CORS 헤더가 붙어 있어야 브라우저 코드가 401을 읽고 로그인 화면으로 보낼 수 있다
 *  - Origin을 보내지 않는 Unity 에디터·PC 빌드 요청에는 아무것도 달라지지 않는다
 */
@SpringBootTest
@AutoConfigureMockMvc
class CorsPreflightTest {

    /** WebGL 빌드를 띄운 페이지 주소라고 가정한다. (게임 서버와 다른 출처) */
    private static final String ORIGIN = "http://localhost:8000";

    @Autowired
    MockMvc mvc;

    @Test
    void 토큰_없는_사전_요청이_인증_검사_전에_통과한다() throws Exception {
        mvc.perform(options("/api/v1/rooms")
                        .header(HttpHeaders.ORIGIN, ORIGIN)
                        .header(HttpHeaders.ACCESS_CONTROL_REQUEST_METHOD, "GET")
                        .header(HttpHeaders.ACCESS_CONTROL_REQUEST_HEADERS, "Authorization, Content-Type"))
                .andExpect(status().isOk())
                .andExpect(header().string(HttpHeaders.ACCESS_CONTROL_ALLOW_ORIGIN, ORIGIN))
                .andExpect(header().string(HttpHeaders.ACCESS_CONTROL_ALLOW_METHODS, containsString("GET")))
                .andExpect(header().string(HttpHeaders.ACCESS_CONTROL_ALLOW_HEADERS, containsStringIgnoringCase("authorization")))
                .andExpect(header().string(HttpHeaders.ACCESS_CONTROL_MAX_AGE, "3600"));
    }

    @Test
    void PATCH_사전_요청도_허용한다() throws Exception {
        mvc.perform(options("/api/users/me/profile-image")
                        .header(HttpHeaders.ORIGIN, ORIGIN)
                        .header(HttpHeaders.ACCESS_CONTROL_REQUEST_METHOD, "PATCH")
                        .header(HttpHeaders.ACCESS_CONTROL_REQUEST_HEADERS, "Authorization, Content-Type"))
                .andExpect(status().isOk())
                .andExpect(header().string(HttpHeaders.ACCESS_CONTROL_ALLOW_ORIGIN, ORIGIN))
                .andExpect(header().string(HttpHeaders.ACCESS_CONTROL_ALLOW_METHODS, containsString("PATCH")));
    }

    @Test
    void 허용하지_않은_메서드의_사전_요청은_거절한다() throws Exception {
        mvc.perform(options("/api/v1/rooms")
                        .header(HttpHeaders.ORIGIN, ORIGIN)
                        .header(HttpHeaders.ACCESS_CONTROL_REQUEST_METHOD, "TRACE"))
                .andExpect(status().isForbidden())
                .andExpect(header().doesNotExist(HttpHeaders.ACCESS_CONTROL_ALLOW_ORIGIN));
    }

    @Test
    void 토큰_없는_실제_요청은_401이지만_CORS_헤더가_붙어_브라우저가_읽을_수_있다() throws Exception {
        mvc.perform(get("/api/v1/rooms").header(HttpHeaders.ORIGIN, ORIGIN))
                .andExpect(status().isUnauthorized())
                .andExpect(header().string(HttpHeaders.ACCESS_CONTROL_ALLOW_ORIGIN, ORIGIN))
                .andExpect(header().string(HttpHeaders.ACCESS_CONTROL_EXPOSE_HEADERS, containsString("Retry-After")));
    }

    @Test
    void Origin을_보내지_않는_요청_Unity_에디터_PC_빌드에는_CORS_헤더가_없다() throws Exception {
        mvc.perform(get("/api/v1/rooms"))
                .andExpect(status().isUnauthorized())
                .andExpect(header().doesNotExist(HttpHeaders.ACCESS_CONTROL_ALLOW_ORIGIN));
    }
}
