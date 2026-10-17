package com.WhoisntCitizen_server.realtime.redis;

import com.WhoisntCitizen_server.realtime.LocalRealtimeNotifier;
import com.WhoisntCitizen_server.realtime.RealtimeNotifier;
import com.WhoisntCitizen_server.realtime.protocol.RealtimeMessageCodec;
import com.WhoisntCitizen_server.realtime.protocol.ServerMessage;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;
import org.springframework.data.redis.core.StringRedisTemplate;

import java.util.Collection;
import java.util.List;
import java.util.Objects;

/**
 * Redis Pub/Sub으로 모든 서버에 신호를 전하는 RealtimeNotifier. (5단계, 서버 여러 대)
 *
 * 메서드 하나를 부르면
 *  1) 이 서버의 연결에는 LocalRealtimeNotifier로 바로 보낸다. (Redis를 거치지 않아 늦지 않고, Redis가 잠깐 끊겨도 같은 서버 사람은 받는다)
 *  2) 같은 내용을 RealtimeSignal로 만들어 채널 하나(mafia.realtime.redis.channel)에 발행한다.
 *  3) 다른 서버들은 RealtimeSignalListener로 받아 deliver()를 부르고, 자기 LocalRealtimeNotifier로 자기 연결에 보낸다.
 *     채널은 이 서버에도 되돌아오는데, origin이 자기 ID라 RealtimeSignalListener가 버린다. (1에서 이미 보냈으므로)
 *
 * 받는 사람을 고르는 일은 받은 서버의 LocalRealtimeNotifier가 한다. 채널은 모든 서버에 똑같이 복사해 줄 뿐이다.
 * 그 순간 연결이 없는 서버는 아무것도 하지 않는다.
 *
 * 발행이 실패해도(Redis 장애) 예외를 밖으로 던지지 않는다. 신호는 "다시 가져가라"는 알림일 뿐이라
 * 게임 진행·방 변경을 실패시키면 안 된다. 다른 서버 사람은 클라이언트의 안전망 조회(15초)나 재연결 GET으로 맞춰진다.
 */
public class RedisRealtimeNotifier implements RealtimeNotifier {

    private static final Logger log = LoggerFactory.getLogger(RedisRealtimeNotifier.class);

    private final LocalRealtimeNotifier local;
    private final RealtimeMessageCodec messageCodec;
    private final RealtimeSignalCodec signalCodec;
    private final StringRedisTemplate redis;
    private final String channel;
    private final String serverId;

    public RedisRealtimeNotifier(LocalRealtimeNotifier local, RealtimeMessageCodec messageCodec,
                                 RealtimeSignalCodec signalCodec, StringRedisTemplate redis,
                                 String channel, String serverId) {
        this.local = Objects.requireNonNull(local, "local");
        this.messageCodec = Objects.requireNonNull(messageCodec, "messageCodec");
        this.signalCodec = Objects.requireNonNull(signalCodec, "signalCodec");
        this.redis = Objects.requireNonNull(redis, "redis");
        this.channel = Objects.requireNonNull(channel, "channel");
        this.serverId = Objects.requireNonNull(serverId, "serverId");
    }

    /** 이 서버의 ID. 채널로 되돌아온 자기 신호를 가려낼 때 쓴다 */
    public String serverId() {
        return serverId;
    }

    public String channel() {
        return channel;
    }

    // ---- 보내는 쪽 (이 서버에서 부른다) ----

    @Override
    public void sendToRoom(long roomId, ServerMessage message) {
        local.sendToRoom(roomId, message);
        publish(RealtimeSignal.room(serverId, roomId, messageCodec.encode(message)));
    }

    @Override
    public void sendToUsersInRoom(long roomId, Collection<Long> userIds, ServerMessage message) {
        if (userIds == null || userIds.isEmpty()) {
            return;
        }
        local.sendToUsersInRoom(roomId, userIds, message);
        publish(RealtimeSignal.usersInRoom(serverId, roomId, List.copyOf(userIds), messageCodec.encode(message)));
    }

    @Override
    public void sendToUser(long userId, ServerMessage message) {
        local.sendToUser(userId, message);
        publish(RealtimeSignal.user(serverId, userId, messageCodec.encode(message)));
    }

    @Override
    public void disconnectUser(long userId, int closeCode, String reason) {
        local.disconnectUser(userId, closeCode, reason);
        publish(RealtimeSignal.disconnectUser(serverId, userId, closeCode, reason));
    }

    @Override
    public void removeUserFromRoom(long roomId, long userId) {
        local.removeUserFromRoom(roomId, userId);
        publish(RealtimeSignal.removeUserFromRoom(serverId, roomId, userId));
    }

    @Override
    public void removeRoom(long roomId) {
        local.removeRoom(roomId);
        publish(RealtimeSignal.removeRoom(serverId, roomId));
    }

    // ---- 받는 쪽 (다른 서버가 보낸 신호, RealtimeSignalListener가 부른다) ----

    /**
     * 다른 서버가 보낸 신호를 이 서버의 연결에 반영한다. 다시 발행하지 않는다. (발행하면 서버끼리 끝없이 주고받는다)
     * @throws RuntimeException 신호의 칸이 비었거나 message를 읽을 수 없을 때. 부르는 쪽(RealtimeSignalListener)이 받아 기록한다
     */
    public void deliver(RealtimeSignal signal) {
        switch (signal.op()) {
            case ROOM -> local.sendToRoom(signal.roomId(), messageCodec.decodeServerMessage(signal.message()));
            case USERS_IN_ROOM -> local.sendToUsersInRoom(signal.roomId(), signal.userIds(),
                    messageCodec.decodeServerMessage(signal.message()));
            case USER -> local.sendToUser(signal.userId(), messageCodec.decodeServerMessage(signal.message()));
            case DISCONNECT_USER -> local.disconnectUser(signal.userId(), signal.closeCode(), signal.reason());
            case REMOVE_USER_FROM_ROOM -> local.removeUserFromRoom(signal.roomId(), signal.userId());
            case REMOVE_ROOM -> local.removeRoom(signal.roomId());
        }
    }

    private void publish(RealtimeSignal signal) {
        try {
            redis.convertAndSend(channel, signalCodec.encode(signal));
        } catch (RuntimeException e) {
            log.warn("[ws] 실시간 신호 발행 실패 (이 서버 연결에는 보냄): op={}, {}", signal.op(), e.toString());
        }
    }
}
