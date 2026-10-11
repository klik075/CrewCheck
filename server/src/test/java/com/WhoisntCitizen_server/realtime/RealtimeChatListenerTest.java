package com.WhoisntCitizen_server.realtime;

import com.WhoisntCitizen_server.chat.event.ChatMessageSavedEvent;
import com.WhoisntCitizen_server.realtime.protocol.RealtimeMessageCodec;
import com.WhoisntCitizen_server.realtime.protocol.ServerMessage;
import org.junit.jupiter.api.Test;

import java.util.Set;

import static org.assertj.core.api.Assertions.assertThat;

class RealtimeChatListenerTest {

    private final RealtimeMessageCodec codec = new RealtimeMessageCodec();
    private final RealtimeSessionRegistry registry = new RealtimeSessionRegistry();
    private final RealtimeChatListener listener = new RealtimeChatListener(new LocalRealtimeNotifier(registry, codec));

    private FakeConnection subscribed(String id, long userId, long roomId) {
        FakeConnection connection = new FakeConnection(id, userId);
        registry.register(connection);
        registry.subscribe(id, roomId);
        return connection;
    }

    @Test
    void 모두에게_보이는_메시지는_방_구독자_전원에게_보낸다() {
        FakeConnection a = subscribed("a", 1, 3);
        FakeConnection b = subscribed("b", 2, 3);
        FakeConnection other = subscribed("c", 3, 4);

        listener.onChatMessageSaved(new ChatMessageSavedEvent(3, 42, null));

        String expected = codec.encode(new ServerMessage.ChatChanged(3, 42));
        assertThat(a.sent).containsExactly(expected);
        assertThat(b.sent).containsExactly(expected);
        assertThat(other.sent).isEmpty();
    }

    @Test
    void 일부에게만_보이는_메시지는_볼_수_있는_사람에게만_보낸다() {
        FakeConnection pirate1 = subscribed("a", 1, 3);
        FakeConnection pirate2 = subscribed("b", 2, 3);
        FakeConnection sailor = subscribed("c", 3, 3);

        listener.onChatMessageSaved(new ChatMessageSavedEvent(3, 43, Set.of(1L, 2L)));

        assertThat(pirate1.sent).hasSize(1);
        assertThat(pirate2.sent).hasSize(1);
        assertThat(sailor.sent).as("볼 수 없는 사람은 신호도 받지 않는다").isEmpty();
    }

    @Test
    void 볼_수_있는_사람이_없으면_아무에게도_보내지_않는다() {
        FakeConnection a = subscribed("a", 1, 3);

        listener.onChatMessageSaved(new ChatMessageSavedEvent(3, 44, Set.of()));

        assertThat(a.sent).isEmpty();
    }
}
