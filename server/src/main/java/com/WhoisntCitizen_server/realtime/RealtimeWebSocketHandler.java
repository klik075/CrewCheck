package com.WhoisntCitizen_server.realtime;

import com.WhoisntCitizen_server.realtime.protocol.ClientMessage;
import com.WhoisntCitizen_server.realtime.protocol.RealtimeMessageCodec;
import com.WhoisntCitizen_server.realtime.protocol.RealtimeMessageException;
import com.WhoisntCitizen_server.realtime.protocol.ServerMessage;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;
import org.springframework.web.socket.CloseStatus;
import org.springframework.web.socket.TextMessage;
import org.springframework.web.socket.WebSocketSession;
import org.springframework.web.socket.handler.ConcurrentWebSocketSessionDecorator;
import org.springframework.web.socket.handler.TextWebSocketHandler;

import java.io.IOException;
import java.time.Clock;
import java.util.Map;
import java.util.Objects;
import java.util.concurrent.ConcurrentHashMap;

/**
 * /ws/v1 연결 하나하나를 처리한다. (4-2: 연결·ping만. 방 구독은 4-3)
 *
 *  - 연결되면: connected(userId, ping 간격)를 보낸다
 *  - ping → pong(서버 시각)
 *  - 잘못된 메시지 → error(code)로 답하고 연결은 유지한다
 *  - 바이너리 메시지는 TextWebSocketHandler가 거절하고 연결을 닫는다
 *
 * 보내기는 ConcurrentWebSocketSessionDecorator로 감싼 세션으로 한다.
 * WebSocket 세션은 동시에 두 스레드가 보내면 안 되는데, 앞으로는 요청 스레드·타이머 스레드 등 여러 곳에서
 * 같은 연결로 신호를 보내게 된다(4-4~4-6). 감싼 세션은 보내기를 한 줄로 세우고, 상대가 너무 느리면 연결을 닫는다.
 */
public class RealtimeWebSocketHandler extends TextWebSocketHandler {

    /** 메시지 하나를 보내는 데 이보다 오래 걸리면(상대가 받지 않음) 연결을 닫는다 */
    static final int SEND_TIME_LIMIT_MILLIS = 5_000;
    /** 아직 못 보낸 메시지가 이만큼 쌓이면 연결을 닫는다 */
    static final int SEND_BUFFER_LIMIT_BYTES = 64 * 1024;

    private static final Logger log = LoggerFactory.getLogger(RealtimeWebSocketHandler.class);

    private final RealtimeMessageCodec codec;
    private final Clock clock;
    /** 세션 id → 보내기용으로 감싼 세션 */
    private final Map<String, WebSocketSession> sessions = new ConcurrentHashMap<>();

    public RealtimeWebSocketHandler(RealtimeMessageCodec codec, Clock clock) {
        this.codec = Objects.requireNonNull(codec, "codec");
        this.clock = Objects.requireNonNull(clock, "clock");
    }

    @Override
    public void afterConnectionEstablished(WebSocketSession session) {
        WebSocketSession safe = new ConcurrentWebSocketSessionDecorator(session, SEND_TIME_LIMIT_MILLIS, SEND_BUFFER_LIMIT_BYTES);
        sessions.put(session.getId(), safe);
        long userId = RealtimeHandshakeInterceptor.userId(session);
        log.info("[ws] 연결: userId={}, session={}, 현재 연결 수={}", userId, session.getId(), sessions.size());
        send(safe, ServerMessage.Connected.of(userId));
    }

    @Override
    protected void handleTextMessage(WebSocketSession session, TextMessage message) {
        WebSocketSession safe = sessions.getOrDefault(session.getId(), session);
        ClientMessage clientMessage;
        try {
            clientMessage = codec.decode(message.getPayload());
        } catch (RealtimeMessageException e) {
            log.debug("[ws] 잘못된 메시지: session={}, code={}", session.getId(), e.code());
            send(safe, e.toMessage());
            return;
        }
        switch (clientMessage) {
            case ClientMessage.Ping ping -> send(safe, new ServerMessage.Pong(clock.millis()));
            // 방 구독은 4-3에서 연결 목록(방 → 연결)과 함께 처리한다
            case ClientMessage.RoomSubscribe subscribe ->
                    log.debug("[ws] 방 구독 요청(아직 처리 안 함): session={}, roomId={}", session.getId(), subscribe.roomId());
            case ClientMessage.RoomUnsubscribe unsubscribe ->
                    log.debug("[ws] 방 구독 해제 요청(아직 처리 안 함): session={}, roomId={}", session.getId(), unsubscribe.roomId());
        }
    }

    @Override
    public void handleTransportError(WebSocketSession session, Throwable exception) {
        // 상대가 갑자기 끊는 등. 이 뒤에 afterConnectionClosed가 불린다
        log.debug("[ws] 전송 오류: session={}, {}", session.getId(), exception.toString());
    }

    @Override
    public void afterConnectionClosed(WebSocketSession session, CloseStatus status) {
        sessions.remove(session.getId());
        Object userId = session.getAttributes().get(RealtimeHandshakeInterceptor.ATTR_USER_ID);
        log.info("[ws] 연결 끊김: userId={}, session={}, code={}, 현재 연결 수={}",
                userId, session.getId(), status.getCode(), sessions.size());
    }

    /** 지금 열려 있는 연결 수 (확인·테스트용) */
    public int connectionCount() {
        return sessions.size();
    }

    private void send(WebSocketSession session, ServerMessage message) {
        if (!session.isOpen()) {
            return;
        }
        try {
            session.sendMessage(new TextMessage(codec.encode(message)));
        } catch (IOException | RuntimeException e) {
            // 보내기 실패는 연결 문제다. 연결은 컨테이너가 정리하고 afterConnectionClosed가 불린다
            log.debug("[ws] 보내기 실패: session={}, {}", session.getId(), e.toString());
        }
    }
}
