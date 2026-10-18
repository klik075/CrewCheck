package com.WhoisntCitizen_server.common.server;

import org.slf4j.Logger;
import org.slf4j.LoggerFactory;
import org.springframework.context.SmartLifecycle;
import org.springframework.data.redis.core.StringRedisTemplate;
import org.springframework.scheduling.TaskScheduler;

import java.time.Duration;
import java.util.Objects;
import java.util.concurrent.ScheduledFuture;

/**
 * 같은 서버 ID를 쓰는 서버가 또 있는지 Redis로 확인한다. (멀티서버 2단계)
 *
 * 왜 필요한가
 *  두 서버에 SERVER_ID를 실수로 똑같이 넣으면, Redis Pub/Sub에서 서로의 신호를 "내가 보낸 것"으로 알고 버린다.
 *  그러면 서버를 건너가는 신호가 조용히 사라진다. 에러가 나지 않아 찾기 어려우므로 켤 때와 켜져 있는 동안 경고한다.
 *
 * 방법: Redis 키 하나에 "이 ID는 지금 이 프로세스가 쓰고 있다"를 적어 둔다.
 *   server:v1:instance:{서버 ID} = {instanceToken}  (TTL 30초, 10초마다 연장)
 *  - 켤 때: 키가 없으면 내 토큰을 적는다. 다른 토큰이 이미 있으면 경고하고, 내 토큰으로 덮어쓴다
 *  - 켜져 있는 동안: 키의 토큰이 내 것이면 TTL만 연장한다. 남의 것이면 경고한다 (같은 ID 서버가 나중에 켜진 경우)
 *  - 끌 때: 키의 토큰이 내 것이면 지운다. (정상 재시작 때 다음 프로세스가 경고를 받지 않게)
 *
 * 경고만 하고 서버를 막지는 않는다. 서버가 비정상 종료된 직후(30초 안)에 다시 켜면 이전 프로세스의 키가 남아 있어
 * 한 번 경고가 날 수 있기 때문이다. (systemd가 5초 뒤 재시작). 이 경우 다음 연장부터는 경고가 나지 않는다.
 *
 * 실시간 신호가 redis일 때만 등록한다. (ServerIdentityConfig) 서버 1대(local)에서는 ID가 겹칠 상대가 없다.
 */
public class ServerInstanceRegistry implements SmartLifecycle {

    private static final Logger log = LoggerFactory.getLogger(ServerInstanceRegistry.class);

    static final String KEY_PREFIX = "server:v1:instance:";
    static final Duration TTL = Duration.ofSeconds(30);
    static final Duration REFRESH_INTERVAL = Duration.ofSeconds(10);

    /** 확인 결과. 테스트와 로그에 쓴다 */
    enum ClaimResult {
        /** 키가 없어서 내 것으로 적었다 */
        CLAIMED,
        /** 이미 내 것이라 TTL만 늘렸다 */
        REFRESHED,
        /** 다른 프로세스가 같은 ID를 쓰고 있다 */
        CONFLICT,
        /** Redis에 묻지 못했다 */
        FAILED
    }

    private final ServerIdentity identity;
    private final StringRedisTemplate redis;
    private final TaskScheduler scheduler;
    private final String key;

    private volatile boolean running;
    private volatile ScheduledFuture<?> refreshTask;
    /** 같은 경고를 10초마다 반복하지 않게, 충돌 상태가 바뀔 때만 남긴다 */
    private volatile boolean conflictReported;

    public ServerInstanceRegistry(ServerIdentity identity, StringRedisTemplate redis, TaskScheduler scheduler) {
        this.identity = Objects.requireNonNull(identity, "identity");
        this.redis = Objects.requireNonNull(redis, "redis");
        this.scheduler = Objects.requireNonNull(scheduler, "scheduler");
        this.key = KEY_PREFIX + identity.id();
    }

    // ---------- 확인 (켤 때 / 주기적으로) ----------

    /** 켤 때 한 번. 다른 프로세스의 키가 있으면 경고하고 내 것으로 덮어쓴다 */
    ClaimResult claimOnStart() {
        try {
            Boolean created = redis.opsForValue().setIfAbsent(key, identity.instanceToken(), TTL);
            if (Boolean.TRUE.equals(created)) {
                return ClaimResult.CLAIMED;
            }
            String owner = redis.opsForValue().get(key);
            if (identity.instanceToken().equals(owner)) {
                redis.expire(key, TTL);
                return ClaimResult.REFRESHED;
            }
            reportConflict(owner, true);
            redis.opsForValue().set(key, identity.instanceToken(), TTL);
            return ClaimResult.CONFLICT;
        } catch (RuntimeException e) {
            log.warn("[server] 서버 ID 등록 실패 (Redis): id={}, {}", identity.id(), e.toString());
            return ClaimResult.FAILED;
        }
    }

    /** 켜져 있는 동안 10초마다. 내 것이면 연장, 없어졌으면 다시 적고, 남의 것이면 경고한다 */
    ClaimResult refresh() {
        try {
            String owner = redis.opsForValue().get(key);
            if (identity.instanceToken().equals(owner)) {
                redis.expire(key, TTL);
                clearConflict();
                return ClaimResult.REFRESHED;
            }
            if (owner == null) {
                // TTL이 지났거나(Redis 재시작 등) 누가 지웠다. 다시 적는다
                Boolean created = redis.opsForValue().setIfAbsent(key, identity.instanceToken(), TTL);
                if (Boolean.TRUE.equals(created)) {
                    clearConflict();
                    return ClaimResult.CLAIMED;
                }
                owner = redis.opsForValue().get(key);
            }
            reportConflict(owner, false);
            return ClaimResult.CONFLICT;
        } catch (RuntimeException e) {
            log.debug("[server] 서버 ID 연장 실패 (Redis): id={}, {}", identity.id(), e.toString());
            return ClaimResult.FAILED;
        }
    }

    /** 끌 때. 내 것일 때만 지운다 (같은 ID의 다른 서버 키를 지우지 않게) */
    void release() {
        try {
            String owner = redis.opsForValue().get(key);
            if (identity.instanceToken().equals(owner)) {
                redis.delete(key);
            }
        } catch (RuntimeException e) {
            log.debug("[server] 서버 ID 해제 실패 (Redis): id={}, {}", identity.id(), e.toString());
        }
    }

    private void reportConflict(String owner, boolean onStart) {
        if (conflictReported && !onStart) {
            return;
        }
        conflictReported = true;
        log.warn("[server] 서버 ID '{}'를 다른 프로세스도 쓰고 있습니다 (상대 토큰 앞 8자리={}). "
                        + "서버마다 SERVER_ID를 다르게 넣으세요. 같으면 서버끼리 실시간 신호를 서로 버립니다. "
                        + "(방금 비정상 종료된 서버를 다시 켠 경우라면 30초 안에 사라집니다)",
                identity.id(), owner == null ? "?" : owner.substring(0, Math.min(8, owner.length())));
    }

    private void clearConflict() {
        if (conflictReported) {
            conflictReported = false;
            log.info("[server] 서버 ID '{}' 충돌이 풀렸습니다", identity.id());
        }
    }

    // ---------- 켜고 끄기 (SmartLifecycle) ----------

    @Override
    public void start() {
        ClaimResult result = claimOnStart();
        log.info("[server] 서버 ID 등록: id={}, 결과={}", identity, result);
        refreshTask = scheduler.scheduleAtFixedRate(this::refresh, REFRESH_INTERVAL);
        running = true;
    }

    @Override
    public void stop() {
        running = false;
        ScheduledFuture<?> task = refreshTask;
        if (task != null) {
            task.cancel(false);
        }
        release();
    }

    @Override
    public boolean isRunning() {
        return running;
    }
}
