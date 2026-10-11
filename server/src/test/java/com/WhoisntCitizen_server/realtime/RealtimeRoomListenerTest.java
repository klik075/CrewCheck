package com.WhoisntCitizen_server.realtime;

import com.WhoisntCitizen_server.game.event.PlayersDepartedEvent;
import com.WhoisntCitizen_server.lobby.event.RoomPlayerKickedEvent;
import com.WhoisntCitizen_server.lobby.event.RoomPlayerLeftEvent;
import com.WhoisntCitizen_server.lobby.event.RoomStateChangedEvent;
import com.WhoisntCitizen_server.realtime.protocol.RealtimeMessageCodec;
import com.WhoisntCitizen_server.realtime.protocol.ServerMessage;
import org.junit.jupiter.api.Test;

import java.util.List;

import static org.assertj.core.api.Assertions.assertThat;

class RealtimeRoomListenerTest {

    private final RealtimeMessageCodec codec = new RealtimeMessageCodec();
    private final RealtimeSessionRegistry registry = new RealtimeSessionRegistry();
    private final RealtimeRoomListener listener = new RealtimeRoomListener(new LocalRealtimeNotifier(registry, codec));

    private FakeConnection subscribed(String id, long userId, long roomId) {
        FakeConnection connection = new FakeConnection(id, userId);
        registry.register(connection);
        registry.subscribe(id, roomId);
        return connection;
    }

    private final String roomChanged3 = codec.encode(new ServerMessage.RoomChanged(3));

    @Test
    void 방이_바뀌면_그_방_구독자에게_room_changed를_보낸다() {
        FakeConnection inRoom = subscribed("a", 1, 3);
        FakeConnection otherRoom = subscribed("b", 2, 4);

        listener.onRoomStateChanged(new RoomStateChangedEvent(3L, false));

        assertThat(inRoom.sent).containsExactly(roomChanged3);
        assertThat(otherRoom.sent).isEmpty();
        assertThat(registry.roomOf("a")).as("방이 남아 있으면 구독도 그대로").hasValue(3);
    }

    @Test
    void 방이_삭제되면_알린_뒤_구독을_모두_푼다() {
        FakeConnection a = subscribed("a", 1, 3);
        FakeConnection b = subscribed("b", 2, 3);

        listener.onRoomStateChanged(new RoomStateChangedEvent(3L, true));

        assertThat(a.sent).containsExactly(roomChanged3);
        assertThat(b.sent).containsExactly(roomChanged3);
        assertThat(registry.connectionsOfRoom(3)).isEmpty();
    }

    @Test
    void 퇴장하면_그_사람의_구독만_풀린다() {
        subscribed("a", 1, 3);
        subscribed("b", 2, 3);

        listener.onPlayerLeft(new RoomPlayerLeftEvent(3L, 1L, "p1"));

        assertThat(registry.connectionsOfRoom(3)).extracting(RealtimeConnection::id).containsExactly("b");
    }

    @Test
    void 강퇴되면_그_사람의_구독이_풀려_이후_신호를_받지_않는다() {
        FakeConnection kicked = subscribed("a", 1, 3);
        subscribed("b", 2, 3);

        listener.onPlayerKicked(new RoomPlayerKickedEvent(3L, 1L, "p1"));
        listener.onRoomStateChanged(new RoomStateChangedEvent(3L, false));

        assertThat(kicked.sent).isEmpty();
    }

    @Test
    void 게임_중_연결이_끊겨_방에서_빠진_사람들의_구독을_푼다() {
        subscribed("a", 1, 3);
        subscribed("b", 2, 3);
        subscribed("c", 3, 3);

        listener.onPlayersDeparted(new PlayersDepartedEvent("g-1", "3", List.of(1L, 2L)));

        assertThat(registry.connectionsOfRoom(3)).extracting(RealtimeConnection::id).containsExactly("c");
    }

    @Test
    void 로비_방이_아닌_게임의_연결_끊김은_무시한다() {
        subscribed("a", 1, 3);

        listener.onPlayersDeparted(new PlayersDepartedEvent("g-1", "dev-room", List.of(1L)));

        assertThat(registry.roomOf("a")).hasValue(3);
    }
}
