package com.WhoisntCitizen_server.common.redis;

import org.redisson.Redisson;
import org.redisson.api.RedissonClient;
import org.redisson.config.Config;
import org.redisson.config.SingleServerConfig;

/**
 * Redisson 연결을 만드는 도우미. 용도별 Redis 그룹(게임, 로비 등)마다 이 메서드로 연결을 하나씩 만든다.
 *
 * redisson-spring-boot-starter를 쓰지 않고 직접 만드는 이유
 *  - 스타터는 Spring의 기본 Redis 연결(RedisConnectionFactory)을 Redisson 것으로 바꿔 등록한다.
 *    그러면 채팅·로비가 쓰는 기존 연결(Lettuce)까지 모르는 사이에 바뀐다.
 *  - 그룹마다 이미 있는 설정(mafia.redis.game.*, spring.data.redis.*)을 그대로 써서 연결을 만들어야
 *    "함께 바뀌어야 하는 것은 같은 그룹"이라는 원칙대로 host만 바꿔 그룹을 옮길 수 있다.
 *
 * 주의: Lettuce(GameRedis)와 달리 Redisson은 만드는 순간 Redis에 접속한다. Redis가 꺼져 있으면 서버 시작이 실패한다.
 * 그래서 이 연결은 Redis 잠금을 쓰도록 설정했을 때만 만든다. (GameLockRedisConfig, RoomLockRedisConfig)
 * 연결을 다 쓰면 shutdown()으로 닫는다. Bean으로 등록할 때는 destroyMethod = "shutdown"을 준다.
 */
public final class RedissonClients {

    // 잠금 용도라 연결이 많이 필요 없다. (기본값은 최소 24개, 최대 64개)
    private static final int CONNECTION_MINIMUM_IDLE = 2;
    private static final int CONNECTION_POOL_SIZE = 16;

    private RedissonClients() {
    }

    /** Redis 한 대(single server)에 접속하는 Redisson 연결을 만든다. password가 비어 있으면 비밀번호 없이 접속한다. */
    public static RedissonClient create(String host, int port, int database, String password) {
        Config config = new Config();
        SingleServerConfig server = config.useSingleServer()
                .setAddress("redis://" + host + ":" + port)
                .setDatabase(database)
                .setConnectionMinimumIdleSize(CONNECTION_MINIMUM_IDLE)
                .setConnectionPoolSize(CONNECTION_POOL_SIZE);
        if (password != null && !password.isBlank()) {
            server.setPassword(password);
        }
        return Redisson.create(config);
    }
}
