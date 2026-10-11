package com.WhoisntCitizen_server.realtime;

import com.WhoisntCitizen_server.global.util.JwtTokenProvider;
import org.junit.jupiter.api.AfterEach;
import org.junit.jupiter.api.Test;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.beans.factory.annotation.Value;
import org.springframework.boot.test.context.SpringBootTest;
import org.springframework.web.socket.TextMessage;
import org.springframework.web.socket.WebSocketHttpHeaders;
import org.springframework.web.socket.WebSocketSession;
import org.springframework.web.socket.client.standard.StandardWebSocketClient;
import org.springframework.web.socket.handler.TextWebSocketHandler;
import tools.jackson.databind.json.JsonMapper;

import java.net.URI;
import java.util.ArrayList;
import java.util.List;
import java.util.Map;
import java.util.concurrent.BlockingQueue;
import java.util.concurrent.ExecutionException;
import java.util.concurrent.LinkedBlockingQueue;
import java.util.concurrent.TimeUnit;

import static org.assertj.core.api.Assertions.assertThat;
import static org.assertj.core.api.Assertions.assertThatThrownBy;

/**
 * 4-2: 실제 서버(임의 포트)를 띄우고 실제 WebSocket 클라이언트로 /ws/v1에 연결해 본다.
 *  - 올바른 토큰(헤더 또는 ?token=)이면 연결되고 connected를 받는다
 *  - ping → pong, 잘못된 메시지 → error (연결 유지)
 *  - 토큰이 없거나, 서명이 다르거나, 만료됐으면 연결이 열리지 않는다 (HTTP 401)
 */
@SpringBootTest(webEnvironment = SpringBootTest.WebEnvironment.RANDOM_PORT)
class RealtimeWebSocketConnectionTest {

    private static final long USER_ID = 15L;

    @Value("${local.server.port}")
    int port;

    @Value("${jwt.secret}")
    String jwtSecret;

    @Autowired
    JwtTokenProvider jwtTokenProvider;

    private final StandardWebSocketClient client = new StandardWebSocketClient();
    private final JsonMapper jsonMapper = JsonMapper.builder().build();
    private final List<WebSocketSession> opened = new ArrayList<>();

    /** 받은 메시지를 순서대로 쌓아 두는 클라이언트 쪽 핸들러 */
    static class Recorder extends TextWebSocketHandler {
        final BlockingQueue<String> messages = new LinkedBlockingQueue<>();

        @Override
        protected void handleTextMessage(WebSocketSession session, TextMessage message) {
            messages.add(message.getPayload());
        }
    }

    @AfterEach
    void closeAll() throws Exception {
        for (WebSocketSession session : opened) {
            if (session.isOpen()) {
                session.close();
            }
        }
    }

    private URI uri(String query) {
        return URI.create("ws://localhost:" + port + "/ws/v1" + query);
    }

    private WebSocketSession connectWithHeader(Recorder recorder, String token) throws Exception {
        WebSocketHttpHeaders headers = new WebSocketHttpHeaders();
        headers.add("Authorization", "Bearer " + token);
        return open(client.execute(recorder, headers, uri("")).get(5, TimeUnit.SECONDS));
    }

    private WebSocketSession connectWithQuery(Recorder recorder, String token) throws Exception {
        return open(client.execute(recorder, new WebSocketHttpHeaders(), uri("?token=" + token)).get(5, TimeUnit.SECONDS));
    }

    private WebSocketSession open(WebSocketSession session) {
        opened.add(session);
        return session;
    }

    @SuppressWarnings("unchecked")
    private Map<String, Object> next(Recorder recorder) throws InterruptedException {
        String text = recorder.messages.poll(5, TimeUnit.SECONDS);
        assertThat(text).as("5초 안에 메시지를 받아야 한다").isNotNull();
        return jsonMapper.readValue(text, Map.class);
    }

    // ---------- 연결 성공 ----------

    @Test
    void Authorization_헤더의_토큰으로_연결하면_connected를_받는다() throws Exception {
        Recorder recorder = new Recorder();
        connectWithHeader(recorder, jwtTokenProvider.createToken(USER_ID, "tester"));

        Map<String, Object> connected = next(recorder);

        assertThat(connected).containsEntry("type", "connected")
                .containsEntry("userId", 15)
                .containsEntry("heartbeatSeconds", 10)
                .containsEntry("idleTimeoutSeconds", 30);
    }

    @Test
    void 쿼리의_토큰으로도_연결할_수_있다() throws Exception {
        Recorder recorder = new Recorder();
        connectWithQuery(recorder, jwtTokenProvider.createToken(USER_ID, "tester"));

        assertThat(next(recorder)).containsEntry("type", "connected").containsEntry("userId", 15);
    }

    @Test
    void ping을_보내면_pong을_받는다() throws Exception {
        Recorder recorder = new Recorder();
        WebSocketSession session = connectWithHeader(recorder, jwtTokenProvider.createToken(USER_ID, "tester"));
        next(recorder); // connected

        session.sendMessage(new TextMessage("{\"type\":\"ping\"}"));

        Map<String, Object> pong = next(recorder);
        assertThat(pong).containsEntry("type", "pong");
        assertThat(((Number) pong.get("serverTime")).longValue()).isPositive();
    }

    @Test
    void 잘못된_메시지에는_error로_답하고_연결은_유지한다() throws Exception {
        Recorder recorder = new Recorder();
        WebSocketSession session = connectWithHeader(recorder, jwtTokenProvider.createToken(USER_ID, "tester"));
        next(recorder); // connected

        session.sendMessage(new TextMessage("hello"));
        assertThat(next(recorder)).containsEntry("type", "error").containsEntry("code", "BAD_MESSAGE");

        session.sendMessage(new TextMessage("{\"type\":\"vote\"}"));
        assertThat(next(recorder)).containsEntry("type", "error").containsEntry("code", "UNKNOWN_TYPE");

        session.sendMessage(new TextMessage("{\"type\":\"ping\"}"));
        assertThat(next(recorder)).containsEntry("type", "pong");
        assertThat(session.isOpen()).isTrue();
    }

    // ---------- 연결 거부 ----------

    @Test
    void 토큰이_없으면_연결되지_않는다() {
        assertThatThrownBy(() -> client.execute(new Recorder(), new WebSocketHttpHeaders(), uri(""))
                .get(5, TimeUnit.SECONDS))
                .isInstanceOf(ExecutionException.class);
    }

    @Test
    void 다른_키로_서명한_토큰이면_연결되지_않는다() {
        JwtTokenProvider otherKey = new JwtTokenProvider("another-secret-key-for-test-only-0123456789abcdef", 3_600_000);
        String forged = otherKey.createToken(USER_ID, "tester");

        assertThatThrownBy(() -> connectWithQuery(new Recorder(), forged)).isInstanceOf(ExecutionException.class);
        assertThatThrownBy(() -> connectWithHeader(new Recorder(), forged)).isInstanceOf(ExecutionException.class);
    }

    @Test
    void 만료된_토큰이면_연결되지_않는다() {
        // JwtDecoder는 시계 오차로 60초까지 봐주므로 그보다 충분히 지난 토큰을 만든다
        JwtTokenProvider expiredIssuer = new JwtTokenProvider(jwtSecret, -10 * 60_000);
        String expired = expiredIssuer.createToken(USER_ID, "tester");

        assertThatThrownBy(() -> connectWithQuery(new Recorder(), expired)).isInstanceOf(ExecutionException.class);
    }

    @Test
    void 토큰_모양이_아니면_연결되지_않는다() {
        assertThatThrownBy(() -> connectWithQuery(new Recorder(), "not-a-jwt")).isInstanceOf(ExecutionException.class);
    }
}
