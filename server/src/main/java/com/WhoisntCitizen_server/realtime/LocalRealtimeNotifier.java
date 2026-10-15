package com.WhoisntCitizen_server.realtime;

import com.WhoisntCitizen_server.realtime.protocol.RealtimeMessageCodec;
import com.WhoisntCitizen_server.realtime.protocol.ServerMessage;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;

import java.util.Collection;
import java.util.List;
import java.util.Set;
import java.util.Objects;

/**
 * 이 서버의 연결 목록으로 바로 보내는 RealtimeNotifier. (서버 1대 기준)
 * 메시지는 한 번만 JSON으로 바꾸고 같은 문자열을 모든 연결에 보낸다.
 */
public class LocalRealtimeNotifier implements RealtimeNotifier {

    private static final Logger log = LoggerFactory.getLogger(LocalRealtimeNotifier.class);

    private final RealtimeSessionRegistry registry;
    private final RealtimeMessageCodec codec;

    public LocalRealtimeNotifier(RealtimeSessionRegistry registry, RealtimeMessageCodec codec) {
        this.registry = Objects.requireNonNull(registry, "registry");
        this.codec = Objects.requireNonNull(codec, "codec");
    }

    @Override
    public void sendToRoom(long roomId, ServerMessage message) {
        sendAll(registry.connectionsOfRoom(roomId), message);
    }

    @Override
    public void sendToUsersInRoom(long roomId, Collection<Long> userIds, ServerMessage message) {
        if (userIds == null || userIds.isEmpty()) {
            return;
        }
        Set<Long> targets = Set.copyOf(userIds);
        sendAll(registry.connectionsOfRoom(roomId).stream()
                .filter(connection -> targets.contains(connection.userId()))
                .toList(), message);
    }

    @Override
    public void sendToUser(long userId, ServerMessage message) {
        sendAll(registry.connectionsOfUser(userId), message);
    }

    @Override
    public void disconnectUser(long userId, int closeCode, String reason) {
        for (RealtimeConnection connection : registry.connectionsOfUser(userId)) {
            connection.close(closeCode, reason);
        }
    }

    @Override
    public void removeUserFromRoom(long roomId, long userId) {
        registry.removeUserFromRoom(roomId, userId);
    }

    @Override
    public void removeRoom(long roomId) {
        registry.removeRoom(roomId);
    }

    private void sendAll(List<RealtimeConnection> connections, ServerMessage message) {
        if (connections.isEmpty()) {
            return;
        }
        String json = codec.encode(message);
        for (RealtimeConnection connection : connections) {
            if (!connection.isOpen()) {
                continue;
            }
            try {
                connection.send(json);
            } catch (RuntimeException e) {
                // 연결 하나의 문제로 나머지 연결에 못 보내는 일이 없게 한다
                log.debug("[ws] 보내기 실패: connection={}, {}", connection.id(), e.toString());
            }
        }
    }
}
