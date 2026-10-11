package com.WhoisntCitizen_server.realtime;

import org.junit.jupiter.api.Test;

import java.util.List;

import static org.assertj.core.api.Assertions.assertThat;

class RealtimeSessionRegistryTest {

    private final RealtimeSessionRegistry registry = new RealtimeSessionRegistry();

    private FakeConnection connect(String id, long userId) {
        FakeConnection connection = new FakeConnection(id, userId);
        registry.register(connection);
        return connection;
    }

    private static List<String> ids(List<RealtimeConnection> connections) {
        return connections.stream().map(RealtimeConnection::id).toList();
    }

    @Test
    void 연결하면_사용자로_찾을_수_있고_방_구독은_없다() {
        connect("s1", 15);

        assertThat(ids(registry.connectionsOfUser(15))).containsExactly("s1");
        assertThat(registry.roomOf("s1")).isEmpty();
        assertThat(registry.connectionCount()).isEqualTo(1);
    }

    @Test
    void 한_사용자가_연결을_여러_개_가질_수_있다() {
        connect("s1", 15);
        connect("s2", 15);
        connect("s3", 16);

        assertThat(ids(registry.connectionsOfUser(15))).containsExactly("s1", "s2");
    }

    @Test
    void 구독하면_방으로_찾을_수_있다() {
        connect("s1", 15);
        connect("s2", 16);

        registry.subscribe("s1", 3);
        registry.subscribe("s2", 3);

        assertThat(ids(registry.connectionsOfRoom(3))).containsExactly("s1", "s2");
        assertThat(registry.roomOf("s1")).hasValue(3);
    }

    @Test
    void 다른_방을_구독하면_이전_방에서_빠진다() {
        connect("s1", 15);
        registry.subscribe("s1", 3);

        registry.subscribe("s1", 4);

        assertThat(registry.connectionsOfRoom(3)).isEmpty();
        assertThat(ids(registry.connectionsOfRoom(4))).containsExactly("s1");
    }

    @Test
    void 구독_해제는_그_방을_구독_중일_때만_푼다() {
        connect("s1", 15);
        registry.subscribe("s1", 4);

        registry.unsubscribe("s1", 3); // 늦게 도착한 예전 방 해제 요청

        assertThat(registry.roomOf("s1")).hasValue(4);

        registry.unsubscribe("s1", 4);

        assertThat(registry.roomOf("s1")).isEmpty();
        assertThat(registry.connectionsOfRoom(4)).isEmpty();
    }

    @Test
    void 연결이_끊기면_사용자와_방_목록에서_모두_빠진다() {
        connect("s1", 15);
        registry.subscribe("s1", 3);

        registry.unregister("s1");

        assertThat(registry.connectionsOfUser(15)).isEmpty();
        assertThat(registry.connectionsOfRoom(3)).isEmpty();
        assertThat(registry.connectionCount()).isZero();
    }

    @Test
    void 등록되지_않은_연결은_구독할_수_없다() {
        assertThat(registry.subscribe("unknown", 3)).isFalse();
        assertThat(registry.connectionsOfRoom(3)).isEmpty();
    }

    @Test
    void 방에서_나간_사용자의_구독을_모두_푼다() {
        connect("s1", 15);
        connect("s2", 15);
        connect("s3", 16);
        registry.subscribe("s1", 3);
        registry.subscribe("s2", 3);
        registry.subscribe("s3", 3);

        int removed = registry.removeUserFromRoom(3, 15);

        assertThat(removed).isEqualTo(2);
        assertThat(ids(registry.connectionsOfRoom(3))).containsExactly("s3");
        assertThat(ids(registry.connectionsOfUser(15))).as("연결 자체는 유지된다").containsExactly("s1", "s2");
    }

    @Test
    void 조회_결과는_복사본이다() {
        connect("s1", 15);
        registry.subscribe("s1", 3);
        List<RealtimeConnection> snapshot = registry.connectionsOfRoom(3);

        registry.unregister("s1");

        assertThat(ids(snapshot)).containsExactly("s1");
    }
}
