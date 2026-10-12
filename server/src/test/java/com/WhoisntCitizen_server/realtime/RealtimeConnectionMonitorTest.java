package com.WhoisntCitizen_server.realtime;

import com.WhoisntCitizen_server.realtime.protocol.RealtimeProtocol;
import com.WhoisntCitizen_server.support.MutableClock;
import org.junit.jupiter.api.Test;

import java.time.Duration;
import java.time.Instant;

import static org.assertj.core.api.Assertions.assertThat;

/**
 * 무응답(30초)·토큰 만료 연결을 닫는지 확인한다. 시간은 MutableClock으로 움직이고, 확인(sweep)은 직접 부른다.
 */
class RealtimeConnectionMonitorTest {

    private static final Instant NOW = Instant.parse("2026-10-11T12:00:00Z");

    private final MutableClock clock = new MutableClock(NOW);
    private final RealtimeConnectionMonitor monitor = new RealtimeConnectionMonitor(null, clock);

    private FakeConnection connect(String id, Instant tokenExpiresAt) {
        FakeConnection connection = new FakeConnection(id, 15);
        monitor.register(connection, tokenExpiresAt);
        return connection;
    }

    private void advance(long seconds) {
        clock.advance(Duration.ofSeconds(seconds));
    }

    @Test
    void 메시지가_30초를_넘게_없으면_4408로_닫는다() {
        FakeConnection connection = connect("a", null);

        advance(30);
        assertThat(monitor.sweep()).as("딱 30초는 아직 닫지 않는다").isZero();

        advance(1);
        assertThat(monitor.sweep()).isEqualTo(1);
        assertThat(connection.closedCode).isEqualTo(RealtimeProtocol.CLOSE_IDLE_TIMEOUT);
    }

    @Test
    void 메시지를_받으면_30초가_다시_시작된다() {
        FakeConnection connection = connect("a", null);

        advance(20);
        monitor.markActive("a");   // ping
        advance(25);               // 마지막 메시지로부터 25초
        monitor.sweep();

        assertThat(connection.isOpen()).isTrue();

        advance(6);                // 31초
        monitor.sweep();

        assertThat(connection.closedCode).isEqualTo(RealtimeProtocol.CLOSE_IDLE_TIMEOUT);
    }

    @Test
    void 토큰이_만료되면_메시지가_와도_4401로_닫는다() {
        FakeConnection connection = connect("a", NOW.plusSeconds(10));

        advance(10);
        monitor.markActive("a");
        monitor.sweep();

        assertThat(connection.closedCode).isEqualTo(RealtimeProtocol.CLOSE_TOKEN_EXPIRED);
    }

    @Test
    void 토큰_만료_전이고_메시지가_오면_닫지_않는다() {
        FakeConnection connection = connect("a", NOW.plusSeconds(3600));

        for (int i = 0; i < 6; i++) {
            advance(10);
            monitor.markActive("a");
            monitor.sweep();
        }

        assertThat(connection.isOpen()).isTrue();
        assertThat(connection.closedCode).isEqualTo(-1);
    }

    @Test
    void 닫은_연결과_끊긴_연결은_더_보지_않는다() {
        connect("a", null);
        connect("b", null);
        monitor.unregister("b");      // 클라이언트가 먼저 끊음

        advance(31);
        assertThat(monitor.sweep()).isEqualTo(1);
        assertThat(monitor.trackedCount()).isZero();

        advance(31);
        assertThat(monitor.sweep()).as("한 번 닫은 연결은 다시 닫지 않는다").isZero();
    }

    @Test
    void 이미_닫힌_연결은_건너뛴다() {
        FakeConnection connection = connect("a", null);
        connection.open = false;

        advance(31);

        assertThat(monitor.sweep()).isZero();
        assertThat(connection.closedCode).isEqualTo(-1);
    }
}
