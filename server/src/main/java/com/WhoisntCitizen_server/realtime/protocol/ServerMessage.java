package com.WhoisntCitizen_server.realtime.protocol;

import com.fasterxml.jackson.annotation.JsonSubTypes;
import com.fasterxml.jackson.annotation.JsonTypeInfo;

/**
 * 서버 → 클라이언트 메시지. JSON 맨 앞에 "type"이 붙는다. (RealtimeMessageCodec.encode)
 *
 *   {"type":"game.changed","gameId":"abc","phaseVersion":7}
 *
 * 변경 알림(*.changed)은 "바뀌었으니 다시 가져가라"는 신호일 뿐 내용이 아니다. 클라이언트는 받으면 해당 GET을 부른다.
 *  - game.changed → GET /api/v1/games/{gameId} (필요하면 /me, /result 등)
 *  - room.changed → GET /api/v1/rooms/{roomId}
 *  - chat.changed → GET /api/v1/rooms/{roomId}/messages?afterId=(마지막으로 받은 id)
 */
@JsonTypeInfo(use = JsonTypeInfo.Id.NAME, include = JsonTypeInfo.As.PROPERTY, property = "type")
@JsonSubTypes({
        @JsonSubTypes.Type(value = ServerMessage.Connected.class, name = ServerMessage.Connected.TYPE),
        @JsonSubTypes.Type(value = ServerMessage.Pong.class, name = ServerMessage.Pong.TYPE),
        @JsonSubTypes.Type(value = ServerMessage.Subscribed.class, name = ServerMessage.Subscribed.TYPE),
        @JsonSubTypes.Type(value = ServerMessage.GameChanged.class, name = ServerMessage.GameChanged.TYPE),
        @JsonSubTypes.Type(value = ServerMessage.RoomChanged.class, name = ServerMessage.RoomChanged.TYPE),
        @JsonSubTypes.Type(value = ServerMessage.ChatChanged.class, name = ServerMessage.ChatChanged.TYPE),
        @JsonSubTypes.Type(value = ServerMessage.ErrorMessage.class, name = ServerMessage.ErrorMessage.TYPE)
})
public sealed interface ServerMessage {

    /**
     * 연결 직후 한 번. 누구로 인증됐는지와 ping 간격을 알려 준다.
     * 클라이언트는 이 메시지를 받은 뒤 보고 있는 방을 room.subscribe로 알린다.
     */
    record Connected(long userId, long heartbeatSeconds, long idleTimeoutSeconds) implements ServerMessage {
        public static final String TYPE = "connected";

        public static Connected of(long userId) {
            return new Connected(userId, RealtimeProtocol.HEARTBEAT_INTERVAL.toSeconds(),
                    RealtimeProtocol.IDLE_TIMEOUT.toSeconds());
        }
    }

    /** ping에 대한 답. serverTime(epoch ms)은 클라이언트가 서버 시계와의 차이를 잴 때 쓸 수 있다 */
    record Pong(long serverTime) implements ServerMessage {
        public static final String TYPE = "pong";
    }

    /**
     * room.subscribe가 받아들여졌다. 이 순간부터 그 방의 신호가 온다.
     * 클라이언트는 이 메시지를 받은 뒤 방·게임·채팅을 GET으로 한 번 가져온다. (구독 전에 일어난 변경을 놓치지 않게)
     */
    record Subscribed(long roomId) implements ServerMessage {
        public static final String TYPE = "subscribed";
    }

    /**
     * 게임 상태가 바뀌었다. (페이즈 전환, 투표, 밤 행동, 연결 끊김, 종료 등)
     * phaseVersion은 참고용이다. 투표처럼 페이즈가 그대로여도 바뀌는 일이 있으니 받으면 항상 다시 가져온다.
     */
    record GameChanged(String gameId, long phaseVersion) implements ServerMessage {
        public static final String TYPE = "game.changed";
    }

    /** 방 정보가 바뀌었다. (입장·퇴장·준비·방장 변경·강퇴·게임 시작·게임 종료 후 복귀·방 삭제) */
    record RoomChanged(long roomId) implements ServerMessage {
        public static final String TYPE = "room.changed";
    }

    /**
     * 방 채팅에 새 메시지가 생겼다. lastMessageId는 새 메시지 중 가장 큰 id.
     * 클라이언트가 이미 그 id까지 받았다면 다시 가져오지 않아도 된다.
     * 해적 전용·사망자 전용·세이렌 팀 전용 메시지는 볼 수 있는 사람에게만 이 신호가 간다. (못 보는 사람은 대화가 오갔다는 것도 모른다)
     */
    record ChatChanged(long roomId, long lastMessageId) implements ServerMessage {
        public static final String TYPE = "chat.changed";
    }

    /** 클라이언트가 보낸 메시지를 처리하지 못했다. 연결은 끊지 않는다 */
    record ErrorMessage(RealtimeErrorCode code, String message) implements ServerMessage {
        public static final String TYPE = "error";
    }
}
