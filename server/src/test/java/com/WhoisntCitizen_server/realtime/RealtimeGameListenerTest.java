package com.WhoisntCitizen_server.realtime;

import com.WhoisntCitizen_server.game.event.GameStateChangedEvent;
import com.WhoisntCitizen_server.realtime.protocol.RealtimeMessageCodec;
import com.WhoisntCitizen_server.realtime.protocol.ServerMessage;
import org.junit.jupiter.api.Test;

import static org.assertj.core.api.Assertions.assertThat;

class RealtimeGameListenerTest {

    private final RealtimeMessageCodec codec = new RealtimeMessageCodec();
    private final RealtimeSessionRegistry registry = new RealtimeSessionRegistry();
    private final RealtimeGameListener listener = new RealtimeGameListener(new LocalRealtimeNotifier(registry, codec));

    private FakeConnection subscribed(String id, long userId, long roomId) {
        FakeConnection connection = new FakeConnection(id, userId);
        registry.register(connection);
        registry.subscribe(id, roomId);
        return connection;
    }

    @Test
    void 게임이_바뀌면_그_방_구독자에게_game_changed를_보낸다() {
        FakeConnection inRoom = subscribed("a", 1, 3);
        FakeConnection otherRoom = subscribed("b", 2, 4);

        listener.onGameStateChanged(new GameStateChangedEvent("g-1", "3", 7));

        assertThat(inRoom.sent).containsExactly(codec.encode(new ServerMessage.GameChanged("g-1", 7)));
        assertThat(otherRoom.sent).isEmpty();
    }

    @Test
    void 로비_방이_아닌_게임이면_보내지_않는다() {
        FakeConnection connection = subscribed("a", 1, 3);

        listener.onGameStateChanged(new GameStateChangedEvent("g-1", "dev-room", 1));
        listener.onGameStateChanged(new GameStateChangedEvent("g-2", null, 1));

        assertThat(connection.sent).isEmpty();
    }
}
