package com.WhoisntCitizen_server.realtime.protocol;

import org.junit.jupiter.api.Test;
import tools.jackson.databind.json.JsonMapper;

import java.util.Map;

import static org.assertj.core.api.Assertions.assertThat;
import static org.assertj.core.api.Assertions.assertThatThrownBy;

/**
 * Unity와 약속한 메시지 모양을 고정하는 테스트. 여기가 깨지면 클라이언트도 같이 고쳐야 한다.
 * (필드 순서는 Jackson 설정에 따라 바뀔 수 있어 Map으로 비교하고, "type"이 맨 앞인 것만 따로 확인한다)
 */
class RealtimeMessageCodecTest {

    private final RealtimeMessageCodec codec = new RealtimeMessageCodec();
    private final JsonMapper reader = JsonMapper.builder().build();

    @SuppressWarnings("unchecked")
    private Map<String, Object> json(String text) {
        return reader.readValue(text, Map.class);
    }

    // ---------- 서버 → 클라이언트 ----------

    @Test
    void 게임_변경_신호() {
        String text = codec.encode(new ServerMessage.GameChanged("abc", 7));

        assertThat(text).startsWith("{\"type\":\"game.changed\"");
        assertThat(json(text)).isEqualTo(Map.of("type", "game.changed", "gameId", "abc", "phaseVersion", 7));
    }

    @Test
    void 방_변경_신호() {
        String text = codec.encode(new ServerMessage.RoomChanged(3));

        assertThat(text).startsWith("{\"type\":\"room.changed\"");
        assertThat(json(text)).isEqualTo(Map.of("type", "room.changed", "roomId", 3));
    }

    @Test
    void 채팅_변경_신호() {
        String text = codec.encode(new ServerMessage.ChatChanged(3, 42));

        assertThat(json(text)).isEqualTo(Map.of("type", "chat.changed", "roomId", 3, "lastMessageId", 42));
    }

    @Test
    void 연결_직후_메시지에는_사용자와_ping_간격이_들어간다() {
        String text = codec.encode(ServerMessage.Connected.of(15));

        assertThat(json(text)).isEqualTo(Map.of("type", "connected", "userId", 15,
                "heartbeatSeconds", 10, "idleTimeoutSeconds", 30));
    }

    @Test
    void pong에는_서버_시각이_들어간다() {
        String text = codec.encode(new ServerMessage.Pong(1791631095000L));

        assertThat(json(text)).isEqualTo(Map.of("type", "pong", "serverTime", 1791631095000L));
    }

    @Test
    void 오류_코드는_이름_그대로_보낸다() {
        String text = codec.encode(new ServerMessage.ErrorMessage(RealtimeErrorCode.NOT_IN_ROOM, "방에 없습니다"));

        assertThat(json(text)).isEqualTo(Map.of("type", "error", "code", "NOT_IN_ROOM", "message", "방에 없습니다"));
    }

    // ---------- 클라이언트 → 서버 ----------

    @Test
    void ping을_읽는다() {
        assertThat(codec.decode("{\"type\":\"ping\"}")).isEqualTo(new ClientMessage.Ping());
    }

    @Test
    void 방_구독과_해제를_읽는다() {
        assertThat(codec.decode("{\"type\":\"room.subscribe\",\"roomId\":3}"))
                .isEqualTo(new ClientMessage.RoomSubscribe(3));
        assertThat(codec.decode("{\"type\":\"room.unsubscribe\",\"roomId\":3}"))
                .isEqualTo(new ClientMessage.RoomUnsubscribe(3));
    }

    @Test
    void 모르는_값은_무시한다() {
        assertThat(codec.decode("{\"type\":\"room.subscribe\",\"roomId\":3,\"clientVersion\":\"1.2\"}"))
                .isEqualTo(new ClientMessage.RoomSubscribe(3));
    }

    @Test
    void 모르는_type이면_UNKNOWN_TYPE() {
        assertCode("{\"type\":\"vote\",\"targetId\":2}", RealtimeErrorCode.UNKNOWN_TYPE);
    }

    @Test
    void JSON이_아니거나_type이_없으면_BAD_MESSAGE() {
        assertCode("hello", RealtimeErrorCode.BAD_MESSAGE);
        assertCode("[1,2]", RealtimeErrorCode.BAD_MESSAGE);
        assertCode("{}", RealtimeErrorCode.BAD_MESSAGE);
        assertCode("", RealtimeErrorCode.BAD_MESSAGE);
        assertCode("null", RealtimeErrorCode.BAD_MESSAGE);
    }

    @Test
    void 구독에_roomId가_없거나_잘못되면_BAD_MESSAGE() {
        assertCode("{\"type\":\"room.subscribe\"}", RealtimeErrorCode.BAD_MESSAGE);
        assertCode("{\"type\":\"room.subscribe\",\"roomId\":0}", RealtimeErrorCode.BAD_MESSAGE);
        assertCode("{\"type\":\"room.subscribe\",\"roomId\":\"abc\"}", RealtimeErrorCode.BAD_MESSAGE);
    }

    @Test
    void 너무_긴_메시지는_읽지_않는다() {
        String longText = "{\"type\":\"ping\",\"pad\":\"" + "x".repeat(RealtimeProtocol.MAX_CLIENT_MESSAGE_CHARS) + "\"}";

        assertCode(longText, RealtimeErrorCode.BAD_MESSAGE);
    }

    @Test
    void 해석_실패는_그대로_error_메시지로_돌려줄_수_있다() {
        RealtimeMessageException e = catchDecode("{\"type\":\"vote\"}");

        assertThat(codec.encode(e.toMessage())).startsWith("{\"type\":\"error\"").contains("UNKNOWN_TYPE");
    }

    private void assertCode(String text, RealtimeErrorCode expected) {
        assertThatThrownBy(() -> codec.decode(text))
                .isInstanceOf(RealtimeMessageException.class)
                .extracting(e -> ((RealtimeMessageException) e).code())
                .isEqualTo(expected);
    }

    private RealtimeMessageException catchDecode(String text) {
        try {
            codec.decode(text);
        } catch (RealtimeMessageException e) {
            return e;
        }
        throw new AssertionError("예외가 나야 합니다: " + text);
    }
}
