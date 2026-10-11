package com.WhoisntCitizen_server.realtime;

import com.WhoisntCitizen_server.realtime.protocol.RealtimeMessageCodec;
import com.WhoisntCitizen_server.realtime.protocol.ServerMessage;
import org.junit.jupiter.api.Test;

import java.util.List;

import static org.assertj.core.api.Assertions.assertThat;
import static org.assertj.core.api.Assertions.assertThatCode;

class LocalRealtimeNotifierTest {

    private final RealtimeMessageCodec codec = new RealtimeMessageCodec();
    private final RealtimeSessionRegistry registry = new RealtimeSessionRegistry();
    private final LocalRealtimeNotifier notifier = new LocalRealtimeNotifier(registry, codec);

    private FakeConnection connect(String id, long userId, Long roomId) {
        FakeConnection connection = new FakeConnection(id, userId);
        registry.register(connection);
        if (roomId != null) {
            registry.subscribe(id, roomId);
        }
        return connection;
    }

    @Test
    void 방에_보내면_그_방을_구독한_연결만_받는다() {
        FakeConnection a = connect("a", 1, 3L);
        FakeConnection b = connect("b", 2, 3L);
        FakeConnection other = connect("c", 3, 4L);
        FakeConnection none = connect("d", 4, null);

        notifier.sendToRoom(3, new ServerMessage.RoomChanged(3));

        String expected = codec.encode(new ServerMessage.RoomChanged(3));
        assertThat(a.sent).containsExactly(expected);
        assertThat(b.sent).containsExactly(expected);
        assertThat(other.sent).isEmpty();
        assertThat(none.sent).isEmpty();
    }

    @Test
    void 사용자에게_보내면_그_사용자의_모든_연결이_받는다() {
        FakeConnection first = connect("a", 1, 3L);
        FakeConnection second = connect("b", 1, null);
        FakeConnection other = connect("c", 2, 3L);

        notifier.sendToUser(1, new ServerMessage.RoomChanged(3));

        assertThat(first.sent).hasSize(1);
        assertThat(second.sent).hasSize(1);
        assertThat(other.sent).isEmpty();
    }

    @Test
    void 방_안의_정해진_사용자에게만_보낼_수_있다() {
        FakeConnection pirate = connect("a", 1, 3L);
        FakeConnection sailor = connect("b", 2, 3L);
        FakeConnection pirateElsewhere = connect("c", 1, 4L); // 같은 사용자의 다른 방 연결

        notifier.sendToUsersInRoom(3, List.of(1L), new ServerMessage.ChatChanged(3, 10));

        assertThat(pirate.sent).hasSize(1);
        assertThat(sailor.sent).isEmpty();
        assertThat(pirateElsewhere.sent).as("그 방을 구독한 연결에만").isEmpty();
    }

    @Test
    void 받을_사용자가_비어_있으면_아무에게도_보내지_않는다() {
        FakeConnection a = connect("a", 1, 3L);

        notifier.sendToUsersInRoom(3, List.of(), new ServerMessage.ChatChanged(3, 10));

        assertThat(a.sent).isEmpty();
    }

    @Test
    void 닫힌_연결에는_보내지_않는다() {
        FakeConnection closed = connect("a", 1, 3L);
        closed.open = false;

        notifier.sendToRoom(3, new ServerMessage.RoomChanged(3));

        assertThat(closed.sent).isEmpty();
    }

    @Test
    void 한_연결에서_보내기가_실패해도_나머지는_받는다() {
        FakeConnection broken = connect("a", 1, 3L);
        broken.failOnSend = true;
        FakeConnection ok = connect("b", 2, 3L);

        notifier.sendToRoom(3, new ServerMessage.RoomChanged(3));

        assertThat(ok.sent).hasSize(1);
    }

    @Test
    void 받을_연결이_없으면_조용히_넘어간다() {
        assertThatCode(() -> notifier.sendToRoom(99, new ServerMessage.RoomChanged(99))).doesNotThrowAnyException();
        assertThatCode(() -> notifier.sendToUser(99, new ServerMessage.RoomChanged(99))).doesNotThrowAnyException();
    }
}
