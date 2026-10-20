package com.WhoisntCitizen_server.realtime;

import com.WhoisntCitizen_server.global.util.JwtTokenProvider;
import org.junit.jupiter.api.AfterEach;
import org.junit.jupiter.api.Test;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.beans.factory.annotation.Value;
import org.springframework.boot.test.context.SpringBootTest;
import org.springframework.web.socket.CloseStatus;
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
import java.util.concurrent.LinkedBlockingQueue;
import java.util.concurrent.TimeUnit;

import static org.assertj.core.api.Assertions.assertThat;

/**
 * 4-2: 실제 서버(임의 포트)를 띄우고 실제 WebSocket 클라이언트로 /ws/v1에 연결해 본다.
 *  - 올바른 토큰(헤더 또는 ?token=)이면 연결되고 connected를 받는다
 *  - ping → pong, 잘못된 메시지 → error (연결 유지)
 *  - 토큰이 없거나, 서명이 다르거나, 만료됐으면 연결은 열리지만 메시지 없이 바로 close 4401로 닫힌다
 *    (HTTP 401은 Unity·브라우저가 구분하지 못해서 4401로 알린다. 헤더 방식도 Spring Security가 먼저 401로 막지 않는다)
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

    @Autowired
    RealtimeSessionRegistry registry;

    private final StandardWebSocketClient client = new StandardWebSocketClient();
    private final JsonMapper jsonMapper = JsonMapper.builder().build();
    private final List<WebSocketSession> opened = new ArrayList<>();

    /** 받은 메시지를 순서대로 쌓아 두는 클라이언트 쪽 핸들러 */
    static class Recorder extends TextWebSocketHandler {
        final BlockingQueue<String> messages = new LinkedBlockingQueue<>();
        final BlockingQueue<CloseStatus> closes = new LinkedBlockingQueue<>();

        @Override
        protected void handleTextMessage(WebSocketSession session, TextMessage message) {
            messages.add(message.getPayload());
        }

        @Override
        public void afterConnectionClosed(WebSocketSession session, CloseStatus status) {
            closes.add(status);
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

    // ---------- 인증 실패: 연결 후 4401로 닫힘 ----------

    /** 서버가 메시지를 하나도 보내지 않고 4401로 닫는지 확인한다 */
    private void assertClosedWith4401(Recorder recorder) throws InterruptedException {
        CloseStatus status = recorder.closes.poll(5, TimeUnit.SECONDS);
        assertThat(status).as("5초 안에 서버가 연결을 닫아야 한다").isNotNull();
        assertThat(status.getCode()).isEqualTo(4401);
        assertThat(recorder.messages).as("connected를 포함해 아무 메시지도 받지 않는다").isEmpty();
    }

    @Test
    void 토큰이_없으면_연결_직후_4401로_닫힌다() throws Exception {
        Recorder recorder = new Recorder();
        open(client.execute(recorder, new WebSocketHttpHeaders(), uri("")).get(5, TimeUnit.SECONDS));

        assertClosedWith4401(recorder);
    }

    @Test
    void Bearer가_아닌_Authorization_헤더면_토큰이_없는_것으로_보고_4401로_닫힌다() throws Exception {
        Recorder recorder = new Recorder();
        WebSocketHttpHeaders headers = new WebSocketHttpHeaders();
        headers.add("Authorization", "Basic dXNlcjpwYXNz");
        open(client.execute(recorder, headers, uri("")).get(5, TimeUnit.SECONDS));

        assertClosedWith4401(recorder);
    }

    @Test
    void 다른_키로_서명한_토큰이면_쿼리든_헤더든_4401로_닫힌다() throws Exception {
        JwtTokenProvider otherKey = new JwtTokenProvider("another-secret-key-for-test-only-0123456789abcdef", 3_600_000);
        String forged = otherKey.createToken(USER_ID, "tester");

        Recorder byQuery = new Recorder();
        connectWithQuery(byQuery, forged);
        assertClosedWith4401(byQuery);

        // 헤더 방식: Spring Security가 /ws/** 의 Bearer 헤더를 읽지 않으므로 HTTP 401이 아니라 연결 후 4401이 된다
        Recorder byHeader = new Recorder();
        connectWithHeader(byHeader, forged);
        assertClosedWith4401(byHeader);
    }

    @Test
    void 만료된_토큰이면_쿼리든_헤더든_4401로_닫힌다() throws Exception {
        // JwtDecoder는 시계 오차로 60초까지 봐주므로 그보다 충분히 지난 토큰을 만든다
        JwtTokenProvider expiredIssuer = new JwtTokenProvider(jwtSecret, -10 * 60_000);
        String expired = expiredIssuer.createToken(USER_ID, "tester");

        Recorder byQuery = new Recorder();
        connectWithQuery(byQuery, expired);
        assertClosedWith4401(byQuery);

        Recorder byHeader = new Recorder();
        connectWithHeader(byHeader, expired);
        assertClosedWith4401(byHeader);
    }

    @Test
    void 토큰_모양이_아니면_4401로_닫힌다() throws Exception {
        Recorder recorder = new Recorder();
        connectWithQuery(recorder, "not-a-jwt");

        assertClosedWith4401(recorder);
    }

    @Test
    void 인증에_실패한_연결은_연결_목록에_등록되지_않는다() throws Exception {
        long forgedUserId = 9_915L; // 다른 테스트의 연결과 섞이지 않는 사용자
        JwtTokenProvider otherKey = new JwtTokenProvider("another-secret-key-for-test-only-0123456789abcdef", 3_600_000);
        Recorder recorder = new Recorder();
        connectWithHeader(recorder, otherKey.createToken(forgedUserId, "tester"));
        assertClosedWith4401(recorder);

        // 다른 테스트의 연결이 늦게 정리될 수 있어 전체 개수 대신 이 사용자의 연결만 본다
        assertThat(registry.connectionsOfUser(forgedUserId)).isEmpty();
    }
}
