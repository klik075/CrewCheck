package com.WhoisntCitizen_server.realtime.protocol;

import com.fasterxml.jackson.annotation.JsonIgnoreProperties;
import tools.jackson.core.JacksonException;
import tools.jackson.databind.ObjectReader;
import tools.jackson.databind.ObjectWriter;
import tools.jackson.databind.json.JsonMapper;

/**
 * 실시간 메시지 ↔ JSON 문자열.
 *
 * Spring의 JsonMapper를 주입받지 않고 직접 만든다. (GameSnapshotCodec과 같은 이유)
 * spring.jackson.* 설정은 REST 응답용이라 바뀔 수 있는데, 그때 Unity와 약속한 메시지 모양까지 바뀌면 안 된다.
 */
public class RealtimeMessageCodec {

    private final JsonMapper jsonMapper = JsonMapper.builder().build();
    /** 인터페이스 타입으로 써야 @JsonTypeInfo가 적용돼 "type"이 맨 앞에 붙는다 */
    private final ObjectWriter serverWriter = jsonMapper.writerFor(ServerMessage.class);
    /** 서버 메시지를 다시 읽을 때. 인터페이스 타입으로 읽어야 "type"을 보고 맞는 record를 고른다 */
    private final ObjectReader serverReader = jsonMapper.readerFor(ServerMessage.class);

    /** 클라이언트 메시지를 한 번에 읽는 그릇. 모르는 값은 무시한다 (클라이언트가 먼저 새 값을 보내도 깨지지 않게) */
    @JsonIgnoreProperties(ignoreUnknown = true)
    record Envelope(String type, Long roomId) {
    }

    public String encode(ServerMessage message) {
        return serverWriter.writeValueAsString(message);
    }

    /**
     * encode로 만든 문자열을 ServerMessage로 되돌린다.
     * 다른 서버가 Redis 채널로 보낸 신호를 이 서버의 연결에 다시 보낼 때 쓴다. (RedisRealtimeNotifier.deliver)
     * @throws IllegalArgumentException 해석할 수 없을 때
     */
    public ServerMessage decodeServerMessage(String json) {
        if (json == null || json.isBlank()) {
            throw new IllegalArgumentException("서버 메시지가 비어 있습니다");
        }
        try {
            return serverReader.readValue(json);
        } catch (JacksonException e) {
            throw new IllegalArgumentException("서버 메시지를 읽을 수 없습니다: " + e.getOriginalMessage(), e);
        }
    }

    /**
     * 클라이언트가 보낸 문자열을 해석한다.
     * @throws RealtimeMessageException 해석할 수 없을 때. code로 이유를 알 수 있다
     */
    public ClientMessage decode(String text) {
        if (text == null || text.isBlank()) {
            throw new RealtimeMessageException(RealtimeErrorCode.BAD_MESSAGE, "빈 메시지입니다");
        }
        if (text.length() > RealtimeProtocol.MAX_CLIENT_MESSAGE_CHARS) {
            throw new RealtimeMessageException(RealtimeErrorCode.BAD_MESSAGE,
                    "메시지가 너무 깁니다 (최대 " + RealtimeProtocol.MAX_CLIENT_MESSAGE_CHARS + "자)");
        }
        Envelope envelope;
        try {
            envelope = jsonMapper.readValue(text, Envelope.class);
        } catch (JacksonException e) {
            throw new RealtimeMessageException(RealtimeErrorCode.BAD_MESSAGE, "JSON 객체가 아니거나 값의 형식이 맞지 않습니다");
        }
        if (envelope == null || envelope.type() == null || envelope.type().isBlank()) {
            throw new RealtimeMessageException(RealtimeErrorCode.BAD_MESSAGE, "type이 없습니다");
        }
        return switch (envelope.type()) {
            case ClientMessage.Ping.TYPE -> new ClientMessage.Ping();
            case ClientMessage.RoomSubscribe.TYPE -> new ClientMessage.RoomSubscribe(requireRoomId(envelope));
            case ClientMessage.RoomUnsubscribe.TYPE -> new ClientMessage.RoomUnsubscribe(requireRoomId(envelope));
            default -> throw new RealtimeMessageException(RealtimeErrorCode.UNKNOWN_TYPE,
                    "알 수 없는 type입니다: " + abbreviate(envelope.type()));
        };
    }

    private static long requireRoomId(Envelope envelope) {
        if (envelope.roomId() == null || envelope.roomId() <= 0) {
            throw new RealtimeMessageException(RealtimeErrorCode.BAD_MESSAGE, envelope.type() + "에는 roomId(양수)가 필요합니다");
        }
        return envelope.roomId();
    }

    /** 이상한 값을 그대로 돌려줄 때 너무 길지 않게 */
    private static String abbreviate(String value) {
        return value.length() <= 40 ? value : value.substring(0, 40) + "...";
    }
}
