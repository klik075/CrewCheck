package com.WhoisntCitizen_server.common.server;

import org.junit.jupiter.api.Test;
import org.springframework.mock.web.MockFilterChain;
import org.springframework.mock.web.MockHttpServletRequest;
import org.springframework.mock.web.MockHttpServletResponse;

import static org.assertj.core.api.Assertions.assertThat;

class ServerIdHeaderFilterTest {

    @Test
    void 응답에_서버_ID_헤더를_붙이고_다음_필터로_넘긴다() throws Exception {
        ServerIdHeaderFilter filter = new ServerIdHeaderFilter(ServerIdentity.resolve("server-a"));
        MockHttpServletResponse response = new MockHttpServletResponse();
        MockFilterChain chain = new MockFilterChain();

        filter.doFilter(new MockHttpServletRequest("GET", "/api/v1/rooms"), response, chain);

        assertThat(response.getHeader(ServerIdHeaderFilter.HEADER)).isEqualTo("server-a");
        assertThat(chain.getRequest()).isNotNull(); // 체인이 이어졌다
    }
}
