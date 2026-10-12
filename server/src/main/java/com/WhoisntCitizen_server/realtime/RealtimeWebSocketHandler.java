package com.WhoisntCitizen_server.realtime;

import com.WhoisntCitizen_server.realtime.protocol.ClientMessage;
import com.WhoisntCitizen_server.realtime.protocol.RealtimeErrorCode;
import com.WhoisntCitizen_server.realtime.protocol.RealtimeMessageCodec;
import com.WhoisntCitizen_server.realtime.protocol.RealtimeMessageException;
import com.WhoisntCitizen_server.realtime.protocol.ServerMessage;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;
import org.springframework.web.socket.CloseStatus;
import org.springframework.web.socket.TextMessage;
import org.springframework.web.socket.WebSocketSession;
import org.springframework.web.socket.handler.TextWebSocketHandler;

import java.time.Clock;
import java.time.Instant;
import java.util.Map;
import java.util.Objects;
import java.util.concurrent.ConcurrentHashMap;

/**
 * /ws/v1 연결 하나하나를 처리한다.
 *
 *  - 연결되면: 연결 목록(RealtimeSessionRegistry)에 넣고 connected를 보낸다
 *  - ping → pong(서버 시각) + 구독 중인 방에 진행 중인 게임이 있으면 접속 기록(touch)
 *  - 어떤 메시지든 받으면 "살아 있음"으로 표시 (30초 동안 없으면 RealtimeConnectionMonitor가 닫는다)
 *  - room.subscribe → 그 방 참가자인지 확인(RoomMembership) → 맞으면 구독하고 subscribed, 아니면 error(NOT_IN_ROOM)
 *  - room.unsubscribe → 구독 해제 (답 없음)
 *  - 잘못된 메시지 → error(code)로 답하고 연결은 유지한다
 *  - 끊기면: 연결 목록에서 뺀다
 *  - 바이너리 메시지는 TextWebSocketHandler가 거절하고 연결을 닫는다
 *
 * 한 연결의 메시지는 컨테이너가 차례로 넘겨주므로, 같은 연결의 subscribe·unsubscribe가 뒤섞이지 않는다.
 */
public class RealtimeWebSocketHandler extends TextWebSocketHandler {

    private static final Logger log = LoggerFactory.getLogger(RealtimeWebSocketHandler.class);

    private final RealtimeMessageCodec codec;
    private final Clock clock;
    private final RealtimeSessionRegistry registry;
    private final RoomMembership membership;
    private final RealtimeConnectionMonitor monitor;
    private final RealtimeActivityRecorder activityRecorder;
    /** 세션 id → 보내기용 연결. 이 핸들러 안에서 답장을 보낼 때 쓴다 */
    private final Map<String, RealtimeConnection> connections = new ConcurrentHashMap<>();

    public RealtimeWebSocketHandler(RealtimeMessageCodec codec, Clock clock,
                                    RealtimeSessionRegistry registry, RoomMembership membership,
                                    RealtimeConnectionMonitor monitor, RealtimeActivityRecorder activityRecorder) {
        this.codec = Objects.requireNonNull(codec, "codec");
        this.clock = Objects.requireNonNull(clock, "clock");
        this.registry = Objects.requireNonNull(registry, "registry");
        this.membership = Objects.requireNonNull(membership, "membership");
        this.monitor = Objects.requireNonNull(monitor, "monitor");
        this.activityRecorder = Objects.requireNonNull(activityRecorder, "activityRecorder");
    }

    @Override
    public void afterConnectionEstablished(WebSocketSession session) {
        long userId = RealtimeHandshakeInterceptor.userId(session);
        RealtimeConnection connection = new WebSocketRealtimeConnection(session, userId);
        connections.put(session.getId(), connection);
        registry.register(connection);
        Object expiresAt = session.getAttributes().get(RealtimeHandshakeInterceptor.ATTR_TOKEN_EXPIRES_AT);
        monitor.register(connection, expiresAt instanceof Instant instant ? instant : null);
        log.info("[ws] 연결: userId={}, session={}, 현재 연결 수={}", userId, session.getId(), registry.connectionCount());
        reply(connection, ServerMessage.Connected.of(userId));
    }

    @Override
    protected void handleTextMessage(WebSocketSession session, TextMessage message) {
        RealtimeConnection connection = connections.get(session.getId());
        if (connection == null) {
            return; // 이미 끊긴 연결
        }
        monitor.markActive(connection.id()); // 잘못된 메시지라도 연결은 살아 있다
        ClientMessage clientMessage;
        try {
            clientMessage = codec.decode(message.getPayload());
        } catch (RealtimeMessageException e) {
            log.debug("[ws] 잘못된 메시지: session={}, code={}", session.getId(), e.code());
            reply(connection, e.toMessage());
            return;
        }
        switch (clientMessage) {
            case ClientMessage.Ping ping -> {
                reply(connection, new ServerMessage.Pong(clock.millis())); // 답을 먼저 보내고
                recordAlive(connection);                                   // 접속 기록은 그 뒤에 (저장소가 느려도 pong은 늦지 않게)
            }
            case ClientMessage.RoomSubscribe subscribe -> subscribe(connection, subscribe.roomId());
            case ClientMessage.RoomUnsubscribe unsubscribe -> registry.unsubscribe(connection.id(), unsubscribe.roomId());
        }
    }

    private void subscribe(RealtimeConnection connection, long roomId) {
        boolean member;
        try {
            member = membership.isMember(roomId, connection.userId());
        } catch (RuntimeException e) {
            log.warn("[ws] 방 참가자 확인 실패: roomId={}, userId={}, {}", roomId, connection.userId(), e.toString());
            reply(connection, new ServerMessage.ErrorMessage(RealtimeErrorCode.SERVER_ERROR,
                    "방 정보를 확인하지 못했습니다. 잠시 뒤 다시 구독해 주세요"));
            return;
        }
        if (!member) {
            log.debug("[ws] 구독 거절: roomId={}, userId={} (방에 없음)", roomId, connection.userId());
            reply(connection, new ServerMessage.ErrorMessage(RealtimeErrorCode.NOT_IN_ROOM,
                    "방 " + roomId + "에 들어 있지 않습니다"));
            return;
        }
        if (registry.subscribe(connection.id(), roomId)) {
            reply(connection, new ServerMessage.Subscribed(roomId));
        }
    }

    /** 구독 중인 방이 있으면 접속 기록을 남긴다. 실패해도 연결에는 영향이 없다 */
    private void recordAlive(RealtimeConnection connection) {
        registry.roomOf(connection.id()).ifPresent(roomId -> {
            try {
                activityRecorder.recordAlive(roomId, connection.userId());
            } catch (RuntimeException e) {
                log.debug("[ws] 접속 기록 실패: roomId={}, userId={}, {}", roomId, connection.userId(), e.toString());
            }
        });
    }

    @Override
    public void handleTransportError(WebSocketSession session, Throwable exception) {
        // 상대가 갑자기 끊는 등. 이 뒤에 afterConnectionClosed가 불린다
        log.debug("[ws] 전송 오류: session={}, {}", session.getId(), exception.toString());
    }

    @Override
    public void afterConnectionClosed(WebSocketSession session, CloseStatus status) {
        connections.remove(session.getId());
        registry.unregister(session.getId());
        monitor.unregister(session.getId());
        Object userId = session.getAttributes().get(RealtimeHandshakeInterceptor.ATTR_USER_ID);
        log.info("[ws] 연결 끊김: userId={}, session={}, code={}, 현재 연결 수={}",
                userId, session.getId(), status.getCode(), registry.connectionCount());
    }

    private void reply(RealtimeConnection connection, ServerMessage message) {
        connection.send(codec.encode(message));
    }
}
