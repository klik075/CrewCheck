package com.WhoisntCitizen_server.realtime;

import com.WhoisntCitizen_server.global.util.JwtTokenProvider;
import com.WhoisntCitizen_server.realtime.protocol.ServerMessage;
import org.junit.jupiter.api.AfterEach;
import org.junit.jupiter.api.BeforeEach;
import org.junit.jupiter.api.Test;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.beans.factory.annotation.Value;
import org.springframework.boot.test.context.SpringBootTest;
import org.springframework.test.context.bean.override.mockito.MockitoBean;
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
import static org.mockito.ArgumentMatchers.anyLong;
import static org.mockito.Mockito.never;
import static org.mockito.Mockito.timeout;
import static org.mockito.Mockito.verify;
import static org.mockito.Mockito.when;

/**
 * 4-3: 실제 WebSocket 연결로 방 구독 → RealtimeNotifier로 보낸 신호가 구독한 연결에만 도착하는지 확인한다.
 * 방 참가자 확인(RoomMembership)은 가짜로 바꾼다. (로비 저장소는 Redis라 이 테스트에서는 쓰지 않는다)
 *  - 사용자 15: 방 3의 참가자
 *  - 사용자 16: 어느 방에도 없음
 */
@SpringBootTest(webEnvironment = SpringBootTest.WebEnvironment.RANDOM_PORT)
class RealtimeRoomSubscriptionTest {

    private static final long ROOM = 3L;
    private static final long MEMBER = 15L;
    private static final long STRANGER = 16L;

    @Value("${local.server.port}")
    int port;

    @Autowired
    JwtTokenProvider jwtTokenProvider;

    @Autowired
    RealtimeNotifier notifier;

    @Autowired
    RealtimeSessionRegistry registry;

    @MockitoBean
    RoomMembership membership;

    @MockitoBean
    RealtimeActivityRecorder activityRecorder;

    private final StandardWebSocketClient client = new StandardWebSocketClient();
    private final JsonMapper jsonMapper = JsonMapper.builder().build();
    private final List<WebSocketSession> opened = new ArrayList<>();

    static class Recorder extends TextWebSocketHandler {
        final BlockingQueue<String> messages = new LinkedBlockingQueue<>();

        @Override
        protected void handleTextMessage(WebSocketSession session, TextMessage message) {
            messages.add(message.getPayload());
        }
    }

    @BeforeEach
    void members() {
        when(membership.isMember(anyLong(), anyLong())).thenReturn(false);
        when(membership.isMember(ROOM, MEMBER)).thenReturn(true);
    }

    @AfterEach
    void closeAll() throws Exception {
        for (WebSocketSession session : opened) {
            if (session.isOpen()) {
                session.close();
            }
        }
    }

    /** 연결하고 connected까지 받은 상태로 돌려준다 */
    private WebSocketSession connect(Recorder recorder, long userId) throws Exception {
        WebSocketHttpHeaders headers = new WebSocketHttpHeaders();
        headers.add("Authorization", "Bearer " + jwtTokenProvider.createToken(userId, "user" + userId));
        WebSocketSession session = client.execute(recorder, headers, URI.create("ws://localhost:" + port + "/ws/v1"))
                .get(5, TimeUnit.SECONDS);
        opened.add(session);
        assertThat(next(recorder)).containsEntry("type", "connected");
        return session;
    }

    @SuppressWarnings("unchecked")
    private Map<String, Object> next(Recorder recorder) throws InterruptedException {
        String text = recorder.messages.poll(5, TimeUnit.SECONDS);
        assertThat(text).as("5초 안에 메시지를 받아야 한다").isNotNull();
        return jsonMapper.readValue(text, Map.class);
    }

    /** 잠깐 기다려도 메시지가 오지 않는지 */
    private void assertNoMessage(Recorder recorder) throws InterruptedException {
        assertThat(recorder.messages.poll(500, TimeUnit.MILLISECONDS)).isNull();
    }

    private static TextMessage subscribe(long roomId) {
        return new TextMessage("{\"type\":\"room.subscribe\",\"roomId\":" + roomId + "}");
    }

    @Test
    void 방_참가자가_구독하면_subscribed를_받고_그_방_신호가_온다() throws Exception {
        Recorder recorder = new Recorder();
        WebSocketSession session = connect(recorder, MEMBER);

        session.sendMessage(subscribe(ROOM));
        assertThat(next(recorder)).containsEntry("type", "subscribed").containsEntry("roomId", 3);

        notifier.sendToRoom(ROOM, new ServerMessage.RoomChanged(ROOM));
        assertThat(next(recorder)).containsEntry("type", "room.changed").containsEntry("roomId", 3);
    }

    @Test
    void 방에_없는_사람은_구독할_수_없고_그_방_신호도_받지_않는다() throws Exception {
        Recorder recorder = new Recorder();
        WebSocketSession session = connect(recorder, STRANGER);

        session.sendMessage(subscribe(ROOM));
        assertThat(next(recorder)).containsEntry("type", "error").containsEntry("code", "NOT_IN_ROOM");

        notifier.sendToRoom(ROOM, new ServerMessage.RoomChanged(ROOM));
        assertNoMessage(recorder);
        assertThat(session.isOpen()).as("거절돼도 연결은 유지된다").isTrue();
    }

    @Test
    void 구독을_해제하면_더는_신호가_오지_않는다() throws Exception {
        Recorder recorder = new Recorder();
        WebSocketSession session = connect(recorder, MEMBER);
        session.sendMessage(subscribe(ROOM));
        next(recorder); // subscribed

        session.sendMessage(new TextMessage("{\"type\":\"room.unsubscribe\",\"roomId\":3}"));
        // 해제는 답이 없으므로 ping-pong으로 서버가 앞의 메시지를 처리했음을 확인한다
        session.sendMessage(new TextMessage("{\"type\":\"ping\"}"));
        assertThat(next(recorder)).containsEntry("type", "pong");

        notifier.sendToRoom(ROOM, new ServerMessage.RoomChanged(ROOM));
        assertNoMessage(recorder);
    }

    @Test
    void 방_구독이_받아들여지면_바로_접속_기록을_남긴다() throws Exception {
        // 다시 연결한 직후 첫 ping(10초 뒤)까지 기록이 비지 않게 (멀티서버 3단계)
        Recorder recorder = new Recorder();
        WebSocketSession session = connect(recorder, MEMBER);

        session.sendMessage(subscribe(ROOM));

        assertThat(next(recorder)).containsEntry("type", "subscribed");
        verify(activityRecorder, timeout(2_000)).recordAlive(ROOM, MEMBER);
    }

    @Test
    void 방을_구독한_채로_ping을_보내면_접속_기록을_남긴다() throws Exception {
        Recorder recorder = new Recorder();
        WebSocketSession session = connect(recorder, MEMBER);
        session.sendMessage(subscribe(ROOM));
        next(recorder); // subscribed

        session.sendMessage(new TextMessage("{\"type\":\"ping\"}"));

        assertThat(next(recorder)).containsEntry("type", "pong");
        // 구독 때 1번 + ping 때 1번
        verify(activityRecorder, timeout(2_000).times(2)).recordAlive(ROOM, MEMBER);
    }

    @Test
    void 구독하지_않은_연결의_ping은_접속_기록을_남기지_않는다() throws Exception {
        Recorder recorder = new Recorder();
        WebSocketSession session = connect(recorder, STRANGER);

        session.sendMessage(new TextMessage("{\"type\":\"ping\"}"));

        assertThat(next(recorder)).containsEntry("type", "pong");
        verify(activityRecorder, never()).recordAlive(anyLong(), anyLong());
    }

    @Test
    void 사용자에게_보내면_방_구독과_상관없이_받는다() throws Exception {
        Recorder recorder = new Recorder();
        connect(recorder, STRANGER);

        notifier.sendToUser(STRANGER, new ServerMessage.RoomChanged(ROOM));

        assertThat(next(recorder)).containsEntry("type", "room.changed");
    }

    @Test
    void 참가자_확인이_실패하면_SERVER_ERROR로_답한다() throws Exception {
        when(membership.isMember(ROOM, MEMBER)).thenThrow(new IllegalStateException("Redis 연결 실패 (테스트)"));
        Recorder recorder = new Recorder();
        WebSocketSession session = connect(recorder, MEMBER);

        session.sendMessage(subscribe(ROOM));

        assertThat(next(recorder)).containsEntry("type", "error").containsEntry("code", "SERVER_ERROR");
    }

    @Test
    void 연결이_끊기면_연결_목록에서_빠진다() throws Exception {
        awaitNoConnections(); // 앞 테스트에서 닫은 연결이 정리될 때까지 (같은 서버를 여러 테스트가 같이 쓴다)
        Recorder recorder = new Recorder();
        WebSocketSession session = connect(recorder, MEMBER);
        session.sendMessage(subscribe(ROOM));
        next(recorder); // subscribed
        assertThat(registry.connectionsOfRoom(ROOM)).hasSize(1);

        session.close();

        awaitNoConnections();
        assertThat(registry.connectionsOfRoom(ROOM)).isEmpty();
        assertThat(registry.connectionsOfUser(MEMBER)).isEmpty();
    }

    /** 서버가 끊긴 연결을 정리하는 데 약간 시간이 걸리므로 최대 5초 기다린다 */
    private void awaitNoConnections() throws InterruptedException {
        long deadline = System.currentTimeMillis() + 5_000;
        while (registry.connectionCount() > 0 && System.currentTimeMillis() < deadline) {
            Thread.sleep(50);
        }
        assertThat(registry.connectionCount()).isZero();
    }
}
