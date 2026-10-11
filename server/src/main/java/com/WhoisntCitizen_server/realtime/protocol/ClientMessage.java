package com.WhoisntCitizen_server.realtime.protocol;

/**
 * 클라이언트 → 서버 메시지. 게임 요청(투표 등)은 여기 없다. 그것은 REST로 보낸다.
 * JSON 모양은 서버 메시지와 같이 "type"을 가진 평평한 객체다. (RealtimeMessageCodec.decode)
 *
 *   {"type":"ping"}
 *   {"type":"room.subscribe","roomId":3}
 *   {"type":"room.unsubscribe","roomId":3}
 */
public sealed interface ClientMessage {

    /** HEARTBEAT_INTERVAL(10초)마다. 서버는 pong으로 답하고 접속 기록을 남긴다 */
    record Ping() implements ClientMessage {
        public static final String TYPE = "ping";
    }

    /**
     * 이 방의 알림(room/game/chat.changed)을 받겠다. 방 화면·게임 화면에 들어갈 때 보낸다.
     * 서버는 실제로 그 방에 들어 있는 사람인지 확인하고, 맞으면 subscribed, 아니면 error(NOT_IN_ROOM)로 답한다.
     * 연결 하나는 방 하나만 구독한다. 다른 방을 구독하면 이전 구독은 풀린다.
     */
    record RoomSubscribe(long roomId) implements ClientMessage {
        public static final String TYPE = "room.subscribe";
    }

    /** 방 알림을 그만 받겠다. 방에서 나갈 때 보낸다. (연결이 끊기면 저절로 풀린다) */
    record RoomUnsubscribe(long roomId) implements ClientMessage {
        public static final String TYPE = "room.unsubscribe";
    }
}
