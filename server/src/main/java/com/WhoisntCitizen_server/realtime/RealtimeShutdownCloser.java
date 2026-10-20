package com.WhoisntCitizen_server.realtime;

import com.WhoisntCitizen_server.realtime.protocol.RealtimeProtocol;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;
import org.springframework.context.SmartLifecycle;

import java.util.List;
import java.util.Objects;

/**
 * 서버가 꺼질 때 이 서버의 WebSocket 연결을 모두 1001(going away)로 닫는다. (멀티서버 2단계, 정상 종료)
 *
 * 왜 필요한가
 *  닫지 않고 프로세스가 끝나면 클라이언트는 TCP가 끊긴 것을 늦게 알아챈다. (ping 응답이 없을 때까지)
 *  그동안 다른 서버에서 오는 신호를 못 받는다. 먼저 닫아 주면 클라이언트가 바로 다시 연결하고,
 *  nginx가 아직 살아 있는 다른 서버로 붙여 준다. (Unity RealtimeFailurePolicy: 1001이면 재연결)
 *
 * 꺼지는 순서 (서버 1대 기준, server.shutdown=graceful)
 *  1) ContextClosedEvent: Spring Boot가 readiness를 REFUSING_TRAFFIC으로 바꾼다 → /actuator/health/readiness가 DOWN
 *  2) 이 클래스 stop() (phase가 가장 높아 먼저): 연결을 모두 1001로 닫는다
 *  3) 웹 서버 정상 종료: 새 연결을 받지 않고, 처리 중인 HTTP 요청을 끝까지 마친다 (최대 spring.lifecycle.timeout-per-shutdown-phase)
 *  4) 타이머·감시·Pub/Sub 구독 정리 (기존 SmartLifecycle들), 마지막으로 Redis 연결 닫기
 *
 * 2)와 3) 사이는 수 밀리초다. 클라이언트는 닫힌 뒤 1초쯤 기다렸다 다시 연결하므로(ReconnectBackoff 첫 대기 1초),
 * 다시 연결할 때는 이 서버가 이미 새 연결을 받지 않는 상태라 nginx가 다른 서버로 보낸다.
 */
public class RealtimeShutdownCloser implements SmartLifecycle {

    private static final Logger log = LoggerFactory.getLogger(RealtimeShutdownCloser.class);

    static final String REASON = "server shutting down";

    private final RealtimeSessionRegistry registry;
    private volatile boolean running;
    /** stop()이 시작되면 true. 연결 끊김 처리(RealtimeWebSocketHandler)가 "서버 사정으로 끊긴 것"인지 알 때 쓴다 */
    private volatile boolean shuttingDown;

    public RealtimeShutdownCloser(RealtimeSessionRegistry registry) {
        this.registry = Objects.requireNonNull(registry, "registry");
    }

    @Override
    public void start() {
        running = true;
    }

    @Override
    public void stop() {
        running = false;
        // 닫기 전에 먼저 표시한다. 닫는 순간 불리는 afterConnectionClosed가 이 값을 보고 접속 상태를 바로 지우지 않는다
        shuttingDown = true;
        List<RealtimeConnection> connections = registry.allConnections();
        int closed = 0;
        for (RealtimeConnection connection : connections) {
            if (!connection.isOpen()) {
                continue;
            }
            try {
                connection.close(RealtimeProtocol.CLOSE_GOING_AWAY, REASON);
                closed++;
            } catch (RuntimeException e) {
                // 하나가 실패해도 나머지는 닫는다
                log.debug("[ws] 종료 중 연결 닫기 실패: connection={}, {}", connection.id(), e.toString());
            }
        }
        log.info("[ws] 서버 종료: WebSocket 연결 {}개를 {}로 닫음", closed, RealtimeProtocol.CLOSE_GOING_AWAY);
    }

    @Override
    public boolean isRunning() {
        return running;
    }

    /** 서버가 꺼지는 중인가 (stop()이 불린 뒤 true). 이 뒤에 끊기는 연결은 모두 서버 사정으로 끊긴 것으로 본다 */
    public boolean isShuttingDown() {
        return shuttingDown;
    }

    /**
     * 가장 먼저 멈춘다. (꺼질 때는 phase가 높은 것부터 멈춘다)
     * 웹 서버 정상 종료(WebServerGracefulShutdownLifecycle, DEFAULT_PHASE - 1024)보다 먼저여야
     * 웹 서버가 "처리 중인 요청"을 기다리는 동안 WebSocket이 남아 있지 않다.
     */
    @Override
    public int getPhase() {
        return SmartLifecycle.DEFAULT_PHASE;
    }
}
