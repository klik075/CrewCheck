package com.WhoisntCitizen_server.realtime;

import org.slf4j.Logger;
import org.slf4j.LoggerFactory;
import org.springframework.web.socket.CloseStatus;
import org.springframework.web.socket.TextMessage;
import org.springframework.web.socket.WebSocketSession;
import org.springframework.web.socket.handler.ConcurrentWebSocketSessionDecorator;

import java.io.IOException;

/**
 * WebSocket 세션 하나를 RealtimeConnection으로 감싼다.
 *
 * 보내기는 ConcurrentWebSocketSessionDecorator로 한다. WebSocket 세션은 두 스레드가 동시에 보내면 안 되는데,
 * 신호는 요청 스레드·타이머 스레드 등 여러 곳에서 같은 연결로 보내진다. 감싼 세션은 보내기를 한 줄로 세우고,
 * 상대가 너무 느리면(5초 넘게 못 보냄, 64KB 넘게 쌓임) 연결을 닫는다. 느린 클라이언트 하나가 서버 스레드를 붙잡지 않게 하기 위해서다.
 */
class WebSocketRealtimeConnection implements RealtimeConnection {

    static final int SEND_TIME_LIMIT_MILLIS = 5_000;
    static final int SEND_BUFFER_LIMIT_BYTES = 64 * 1024;

    private static final Logger log = LoggerFactory.getLogger(WebSocketRealtimeConnection.class);

    private final WebSocketSession session;
    private final long userId;

    WebSocketRealtimeConnection(WebSocketSession rawSession, long userId) {
        this.session = new ConcurrentWebSocketSessionDecorator(rawSession, SEND_TIME_LIMIT_MILLIS, SEND_BUFFER_LIMIT_BYTES);
        this.userId = userId;
    }

    @Override
    public String id() {
        return session.getId();
    }

    @Override
    public long userId() {
        return userId;
    }

    @Override
    public boolean isOpen() {
        return session.isOpen();
    }

    @Override
    public void send(String json) {
        if (!session.isOpen()) {
            return;
        }
        try {
            session.sendMessage(new TextMessage(json));
        } catch (IOException | RuntimeException e) {
            // 보내기 실패는 연결 문제다. 연결은 컨테이너가 정리하고 afterConnectionClosed에서 목록에서 빠진다
            log.debug("[ws] 보내기 실패: session={}, {}", session.getId(), e.toString());
        }
    }

    @Override
    public void close(int code, String reason) {
        try {
            session.close(new CloseStatus(code, reason));
        } catch (IOException | RuntimeException e) {
            log.debug("[ws] 닫기 실패: session={}, {}", session.getId(), e.toString());
        }
    }
}
