package com.WhoisntCitizen_server.realtime;

import com.WhoisntCitizen_server.realtime.protocol.RealtimeProtocol;
import org.junit.jupiter.api.Test;
import org.springframework.context.SmartLifecycle;

import static org.assertj.core.api.Assertions.assertThat;

class RealtimeShutdownCloserTest {

    private final RealtimeSessionRegistry registry = new RealtimeSessionRegistry();
    private final RealtimeShutdownCloser closer = new RealtimeShutdownCloser(registry);

    private FakeConnection connect(String id, long userId) {
        FakeConnection connection = new FakeConnection(id, userId);
        registry.register(connection);
        return connection;
    }

    @Test
    void 끌_때_모든_연결을_1001로_닫는다() {
        FakeConnection a = connect("a", 1);
        FakeConnection b = connect("b", 2);
        registry.subscribe("b", 3);
        closer.start();

        closer.stop();

        assertThat(a.closedCode).isEqualTo(RealtimeProtocol.CLOSE_GOING_AWAY);
        assertThat(b.closedCode).isEqualTo(RealtimeProtocol.CLOSE_GOING_AWAY);
        assertThat(closer.isRunning()).isFalse();
    }

    @Test
    void 닫기_전에_종료_중_표시를_켠다() {
        // 연결을 닫는 순간 불리는 끊김 처리가 이 표시를 봐야 접속 상태를 바로 지우지 않는다
        FakeConnection a = new FakeConnection("a", 1) {
            @Override
            public void close(int code, String reason) {
                assertThat(closer.isShuttingDown()).isTrue();
                super.close(code, reason);
            }
        };
        registry.register(a);
        assertThat(closer.isShuttingDown()).isFalse();

        closer.stop();

        assertThat(closer.isShuttingDown()).isTrue();
        assertThat(a.closedCode).isEqualTo(RealtimeProtocol.CLOSE_GOING_AWAY);
    }

    @Test
    void 이미_닫힌_연결은_건너뛴다() {
        FakeConnection closed = connect("a", 1);
        closed.open = false;

        closer.stop();

        assertThat(closed.closedCode).isEqualTo(-1);
    }

    @Test
    void 웹_서버_정상_종료보다_먼저_멈춘다() {
        // 꺼질 때는 phase가 높은 것부터 멈춘다. 웹 서버 정상 종료는 DEFAULT_PHASE - 1024
        assertThat(closer.getPhase()).isGreaterThan(SmartLifecycle.DEFAULT_PHASE - 1024);
    }
}
