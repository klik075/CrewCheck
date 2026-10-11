package com.WhoisntCitizen_server.realtime.protocol;

import org.junit.jupiter.api.Test;
import org.springframework.http.HttpHeaders;

import java.net.URI;

import static org.assertj.core.api.Assertions.assertThat;

class RealtimeTokenResolverTest {

    private static final URI PLAIN = URI.create("ws://localhost:8080/ws/v1");

    private static HttpHeaders bearer(String value) {
        HttpHeaders headers = new HttpHeaders();
        headers.set(HttpHeaders.AUTHORIZATION, value);
        return headers;
    }

    @Test
    void Authorization_헤더의_Bearer_토큰을_꺼낸다() {
        assertThat(RealtimeTokenResolver.resolve(bearer("Bearer abc.def.ghi"), PLAIN)).contains("abc.def.ghi");
        assertThat(RealtimeTokenResolver.resolve(bearer("bearer abc.def.ghi"), PLAIN)).contains("abc.def.ghi");
    }

    @Test
    void 헤더가_없으면_쿼리의_token을_꺼낸다() {
        URI uri = URI.create("ws://localhost:8080/ws/v1?token=abc.def.ghi");

        assertThat(RealtimeTokenResolver.resolve(new HttpHeaders(), uri)).contains("abc.def.ghi");
    }

    @Test
    void 헤더와_쿼리가_모두_있으면_헤더를_쓴다() {
        URI uri = URI.create("ws://localhost:8080/ws/v1?token=from-query");

        assertThat(RealtimeTokenResolver.resolve(bearer("Bearer from-header"), uri)).contains("from-header");
    }

    @Test
    void Bearer가_아닌_헤더는_받지_않는다() {
        URI uri = URI.create("ws://localhost:8080/ws/v1?token=from-query");

        assertThat(RealtimeTokenResolver.resolve(bearer("Basic dXNlcjpwdw=="), uri)).isEmpty();
    }

    @Test
    void 토큰이_없거나_비어_있으면_없다() {
        assertThat(RealtimeTokenResolver.resolve(new HttpHeaders(), PLAIN)).isEmpty();
        assertThat(RealtimeTokenResolver.resolve(new HttpHeaders(), URI.create("ws://localhost:8080/ws/v1?token="))).isEmpty();
        assertThat(RealtimeTokenResolver.resolve(bearer("Bearer   "), PLAIN)).isEmpty();
        assertThat(RealtimeTokenResolver.resolve(null, null)).isEmpty();
    }
}
