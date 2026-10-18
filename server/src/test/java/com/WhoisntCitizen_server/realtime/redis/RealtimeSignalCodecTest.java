package com.WhoisntCitizen_server.realtime.redis;

import com.WhoisntCitizen_server.realtime.protocol.RealtimeMessageCodec;
import com.WhoisntCitizen_server.realtime.protocol.ServerMessage;
import org.junit.jupiter.api.Test;

import java.util.List;

import static org.assertj.core.api.Assertions.assertThat;
import static org.assertj.core.api.Assertions.assertThatThrownBy;

/** 서버끼리 주고받는 신호의 JSON 모양과, 읽을 수 없는 신호를 막는지 확인한다. (Redis 없이) */
class RealtimeSignalCodecTest {

    private final RealtimeSignalCodec codec = new RealtimeSignalCodec();
    private final RealtimeMessageCodec messageCodec = new RealtimeMessageCodec();

    @Test
    void 보내기_신호는_그대로_되돌아온다() {
        String message = messageCodec.encode(new ServerMessage.ChatChanged(3, 42));
        RealtimeSignal signal = RealtimeSignal.usersInRoom("A", 3, List.of(1L, 2L), message);

        RealtimeSignal decoded = codec.decode(codec.encode(signal));

        assertThat(decoded).isEqualTo(signal);
        assertThat(messageCodec.decodeServerMessage(decoded.message())).isEqualTo(new ServerMessage.ChatChanged(3, 42));
    }

    @Test
    void 로비_신호는_제외할_사용자와_함께_되돌아온다() {
        String message = messageCodec.encode(new ServerMessage.RoomCreated(9));
        RealtimeSignal signal = RealtimeSignal.lobby("A", 1, message);

        RealtimeSignal decoded = codec.decode(codec.encode(signal));

        assertThat(decoded).isEqualTo(signal);
        assertThat(decoded.op()).isEqualTo(RealtimeSignal.Op.LOBBY);
        assertThat(decoded.userId()).isEqualTo(1L);
        assertThat(messageCodec.decodeServerMessage(decoded.message())).isEqualTo(new ServerMessage.RoomCreated(9));
    }

    @Test
    void 빈_칸은_JSON에_넣지_않는다() {
        String json = codec.encode(RealtimeSignal.removeRoom("A", 7));

        // 칸 순서는 Jackson 설정에 따라 달라질 수 있어 내용만 본다
        assertThat(json).contains("\"v\":1", "\"origin\":\"A\"", "\"op\":\"REMOVE_ROOM\"", "\"roomId\":7")
                .doesNotContain("userId", "userIds", "closeCode", "reason", "message");
    }

    @Test
    void 모르는_칸은_무시한다() {
        RealtimeSignal decoded = codec.decode("{\"v\":1,\"origin\":\"A\",\"op\":\"REMOVE_ROOM\",\"roomId\":7,\"newField\":true}");

        assertThat(decoded).isEqualTo(RealtimeSignal.removeRoom("A", 7));
    }

    @Test
    void 모르는_op_다른_버전_깨진_JSON은_막는다() {
        assertThatThrownBy(() -> codec.decode("{\"v\":1,\"origin\":\"A\",\"op\":\"NEW_OP\"}"))
                .isInstanceOf(IllegalArgumentException.class);
        assertThatThrownBy(() -> codec.decode("{\"v\":2,\"origin\":\"A\",\"op\":\"REMOVE_ROOM\",\"roomId\":7}"))
                .isInstanceOf(IllegalArgumentException.class);
        assertThatThrownBy(() -> codec.decode("{\"v\":1,\"op\":\"REMOVE_ROOM\",\"roomId\":7}"))
                .isInstanceOf(IllegalArgumentException.class);
        assertThatThrownBy(() -> codec.decode("not json"))
                .isInstanceOf(IllegalArgumentException.class);
    }

    @Test
    void 모든_서버_메시지를_다시_읽을_수_있다() {
        List<ServerMessage> messages = List.of(
                new ServerMessage.GameChanged("g1", 7),
                new ServerMessage.RoomChanged(3),
                new ServerMessage.ChatChanged(3, 42),
                new ServerMessage.Subscribed(3),
                new ServerMessage.RoomCreated(9));

        for (ServerMessage message : messages) {
            assertThat(messageCodec.decodeServerMessage(messageCodec.encode(message))).isEqualTo(message);
        }
    }
}
