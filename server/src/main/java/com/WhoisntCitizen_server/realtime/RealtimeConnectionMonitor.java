package com.WhoisntCitizen_server.realtime;

import com.WhoisntCitizen_server.realtime.protocol.RealtimeProtocol;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;
import org.springframework.context.SmartLifecycle;
import org.springframework.scheduling.TaskScheduler;

import java.time.Clock;
import java.time.Duration;
import java.time.Instant;
import java.util.ArrayList;
import java.util.List;
import java.util.Map;
import java.util.Objects;
import java.util.concurrent.ConcurrentHashMap;
import java.util.concurrent.ScheduledFuture;

/**
 * 연결이 살아 있는지 주기적으로 확인하고, 아니면 닫는다.
 * [서버 여러 대] 주기 작업 — 서버마다 돌아야 한다. 이 서버에 붙은 연결만 보고 닫기 때문이다. (다른 서버의 연결은 그 서버가 맡는다)
 *
 *  - IDLE_TIMEOUT(30초) 동안 클라이언트 메시지(ping 포함)가 하나도 없으면 → close 4408
 *    (앱이 멈추거나 네트워크가 끊겨도 TCP 연결은 한동안 열린 채로 남는다. 그런 "죽은 연결"에 계속 신호를 보내지 않게)
 *  - 연결할 때 받은 토큰이 만료되면 → close 4401 (클라이언트는 다시 로그인한 뒤 연결)
 *
 * SWEEP_INTERVAL(5초)마다 확인하므로 실제로 닫히는 시점은 기준보다 최대 5초 늦을 수 있다.
 * 연결이 닫혀도 게임에서 바로 빠지지 않는다. 연결 끊김 판정은 기존처럼 마지막 요청 후 60초(InactivePlayerMonitor)다.
 *
 * Spring이 SmartLifecycle로 켜고 끈다. 확인은 gamePhaseScheduler 스레드에서 짧게 돈다. (닫기는 바로 돌아온다)
 */
public class RealtimeConnectionMonitor implements SmartLifecycle {

    static final Duration SWEEP_INTERVAL = Duration.ofSeconds(5);

    private static final Logger log = LoggerFactory.getLogger(RealtimeConnectionMonitor.class);

    /** 확인 대상 연결. lastActive는 메시지를 받을 때마다 바뀐다 */
    private static final class Tracked {
        final RealtimeConnection connection;
        final Instant tokenExpiresAt;
        volatile Instant lastActive;

        Tracked(RealtimeConnection connection, Instant tokenExpiresAt, Instant now) {
            this.connection = connection;
            this.tokenExpiresAt = tokenExpiresAt;
            this.lastActive = now;
        }
    }

    private final Map<String, Tracked> tracked = new ConcurrentHashMap<>();
    private final TaskScheduler scheduler;
    private final Clock clock;
    private final Duration idleTimeout;
    private volatile ScheduledFuture<?> task;

    public RealtimeConnectionMonitor(TaskScheduler scheduler, Clock clock) {
        this(scheduler, clock, RealtimeProtocol.IDLE_TIMEOUT);
    }

    RealtimeConnectionMonitor(TaskScheduler scheduler, Clock clock, Duration idleTimeout) {
        this.scheduler = scheduler;
        this.clock = Objects.requireNonNull(clock, "clock");
        this.idleTimeout = Objects.requireNonNull(idleTimeout, "idleTimeout");
    }

    /** 연결되면 부른다. tokenExpiresAt은 없을 수 있다(null) */
    public void register(RealtimeConnection connection, Instant tokenExpiresAt) {
        tracked.put(connection.id(), new Tracked(connection, tokenExpiresAt, clock.instant()));
    }

    /** 클라이언트 메시지를 받을 때마다 부른다 */
    public void markActive(String connectionId) {
        Tracked t = tracked.get(connectionId);
        if (t != null) {
            t.lastActive = clock.instant();
        }
    }

    /** 연결이 끊기면 부른다 */
    public void unregister(String connectionId) {
        tracked.remove(connectionId);
    }

    /**
     * 지금 시각 기준으로 닫아야 할 연결을 닫는다. 주기적으로 불린다. (테스트에서는 직접 부른다)
     * @return 닫은 연결 수
     */
    public int sweep() {
        Instant now = clock.instant();
        List<Tracked> toClose = new ArrayList<>();
        List<Integer> codes = new ArrayList<>();
        for (Tracked t : tracked.values()) {
            if (!t.connection.isOpen()) {
                continue; // 곧 afterConnectionClosed가 정리한다
            }
            if (t.tokenExpiresAt != null && !now.isBefore(t.tokenExpiresAt)) {
                toClose.add(t);
                codes.add(RealtimeProtocol.CLOSE_TOKEN_EXPIRED);
            } else if (Duration.between(t.lastActive, now).compareTo(idleTimeout) > 0) {
                toClose.add(t);
                codes.add(RealtimeProtocol.CLOSE_IDLE_TIMEOUT);
            }
        }
        for (int i = 0; i < toClose.size(); i++) {
            Tracked t = toClose.get(i);
            int code = codes.get(i);
            String reason = code == RealtimeProtocol.CLOSE_TOKEN_EXPIRED ? "token expired" : "idle timeout";
            log.info("[ws] 연결 닫기: userId={}, connection={}, 이유={}", t.connection.userId(), t.connection.id(), reason);
            tracked.remove(t.connection.id());
            t.connection.close(code, reason);
        }
        return toClose.size();
    }

    int trackedCount() {
        return tracked.size();
    }

    private void sweepSafely() {
        try {
            sweep();
        } catch (RuntimeException e) {
            log.warn("[ws] 연결 확인 실패: {}", e.toString());
        }
    }

    // ---------- 켜고 끄기 (SmartLifecycle) ----------

    @Override
    public void start() {
        if (scheduler != null && task == null) {
            task = scheduler.scheduleWithFixedDelay(this::sweepSafely, SWEEP_INTERVAL);
        }
    }

    @Override
    public void stop() {
        ScheduledFuture<?> current = task;
        task = null;
        if (current != null) {
            current.cancel(false);
        }
    }

    @Override
    public boolean isRunning() {
        return task != null;
    }
}
