package com.WhoisntCitizen_server.realtime;

import com.WhoisntCitizen_server.global.util.JwtTokenProvider;
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

import java.net.URI;
import java.util.ArrayList;
import java.util.List;
import java.util.OptionalLong;
import java.util.concurrent.BlockingQueue;
import java.util.concurrent.LinkedBlockingQueue;
import java.util.concurrent.TimeUnit;

import static org.assertj.core.api.Assertions.assertThat;
import static org.mockito.ArgumentMatchers.any;
import static org.mockito.ArgumentMatchers.anyLong;
import static org.mockito.ArgumentMatchers.eq;
import static org.mockito.Mockito.doThrow;
import static org.mockito.Mockito.never;
import static org.mockito.Mockito.timeout;
import static org.mockito.Mockito.verify;
import static org.mockito.Mockito.when;

/**
 * 실제 WebSocket 연결로 유저 접속 상태 기록(RealtimePresenceRecorder)이 알맞은 때 불리는지 확인한다.
 * 기록 구현(Redis)과 방 참가자 확인은 가짜로 바꾼다. 상태 계산 자체는 UserPresenceRecorderTest가 본다.
 */
@SpringBootTest(webEnvironment = SpringBootTest.WebEnvironment.RANDOM_PORT)
class RealtimePresenceWiringTest {

    private static final long ROOM = 3L;
    private static final long MEMBER = 15L;

    @Value("${local.server.port}")
    int port;

    @Autowired
    JwtTokenProvider jwtTokenProvider;

    @MockitoBean
    RoomMembership membership;

    @MockitoBean
    RealtimeActivityRecorder activityRecorder;

    @MockitoBean
    RealtimePresenceRecorder presenceRecorder;

    private final StandardWebSocketClient client = new StandardWebSocketClient();
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

    private WebSocketSession connect(Recorder recorder, long userId) throws Exception {
        WebSocketHttpHeaders headers = new WebSocketHttpHeaders();
        headers.add("Authorization", "Bearer " + jwtTokenProvider.createToken(userId, "user" + userId));
        WebSocketSession session = client.execute(recorder, headers, URI.create("ws://localhost:" + port + "/ws/v1"))
                .get(5, TimeUnit.SECONDS);
        opened.add(session);
        assertThat(recorder.messages.poll(5, TimeUnit.SECONDS)).contains("connected");
        return session;
    }

    private static String next(Recorder recorder) throws InterruptedException {
        String text = recorder.messages.poll(5, TimeUnit.SECONDS);
        assertThat(text).as("5초 안에 메시지를 받아야 한다").isNotNull();
        return text;
    }

    @Test
    void 연결하면_방_없이_기록하고_끊기면_오프라인으로_바꾼다() throws Exception {
        WebSocketSession session = connect(new Recorder(), MEMBER);

        verify(presenceRecorder, timeout(2000)).update(MEMBER, OptionalLong.empty());

        session.close();
        verify(presenceRecorder, timeout(2000)).offline(MEMBER);
    }

    @Test
    void 방을_구독하면_그_방으로_기록하고_해제하면_방_없이_기록한다() throws Exception {
        Recorder recorder = new Recorder();
        WebSocketSession session = connect(recorder, MEMBER);

        session.sendMessage(new TextMessage("{\"type\":\"room.subscribe\",\"roomId\":3}"));
        assertThat(next(recorder)).contains("subscribed");
        verify(presenceRecorder, timeout(2000)).update(MEMBER, OptionalLong.of(ROOM));

        session.sendMessage(new TextMessage("{\"type\":\"room.unsubscribe\",\"roomId\":3}"));
        // 해제는 답이 없으므로 ping-pong으로 서버가 앞의 메시지를 처리했음을 확인한다
        session.sendMessage(new TextMessage("{\"type\":\"ping\"}"));
        assertThat(next(recorder)).contains("pong");

        // 연결 때 1번 + 해제 때 1번 + ping 때 1번 = 방 없이 3번
        verify(presenceRecorder, timeout(2000).times(3)).update(MEMBER, OptionalLong.empty());
    }

    @Test
    void ping을_보낼_때마다_접속_상태를_갱신한다() throws Exception {
        Recorder recorder = new Recorder();
        WebSocketSession session = connect(recorder, MEMBER);
        verify(presenceRecorder, timeout(2000)).update(MEMBER, OptionalLong.empty()); // 연결 때

        session.sendMessage(new TextMessage("{\"type\":\"ping\"}"));
        assertThat(next(recorder)).contains("pong");

        verify(presenceRecorder, timeout(2000).times(2)).update(MEMBER, OptionalLong.empty());
    }

    @Test
    void 같은_유저의_다른_연결이_남아_있으면_오프라인으로_바꾸지_않는다() throws Exception {
        WebSocketSession first = connect(new Recorder(), MEMBER);
        connect(new Recorder(), MEMBER);

        first.close();
        // 첫 연결이 닫힌 처리가 끝날 시간을 준다. 두 번째 연결이 남아 있으므로 offline은 불리지 않아야 한다
        Thread.sleep(500);
        verify(presenceRecorder, never()).offline(anyLong());
    }

    @Test
    void 접속_상태_기록이_실패해도_연결과_pong은_정상이다() throws Exception {
        doThrow(new IllegalStateException("Redis 연결 실패 (테스트)")).when(presenceRecorder).update(anyLong(), any());
        doThrow(new IllegalStateException("Redis 연결 실패 (테스트)")).when(presenceRecorder).offline(anyLong());

        Recorder recorder = new Recorder();
        WebSocketSession session = connect(recorder, MEMBER);
        session.sendMessage(new TextMessage("{\"type\":\"ping\"}"));

        assertThat(next(recorder)).contains("pong");
        assertThat(session.isOpen()).isTrue();
        session.close();
        verify(presenceRecorder, timeout(2000)).offline(eq(MEMBER));
    }
}
