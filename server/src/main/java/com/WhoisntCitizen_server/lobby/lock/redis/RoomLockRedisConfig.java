package com.WhoisntCitizen_server.lobby.lock.redis;

import com.WhoisntCitizen_server.common.redis.RedissonClients;
import org.redisson.api.RedissonClient;
import org.springframework.beans.factory.annotation.Value;
import org.springframework.boot.autoconfigure.condition.ConditionalOnProperty;
import org.springframework.context.annotation.Bean;
import org.springframework.context.annotation.Configuration;

/**
 * 로비 그룹 Redisson 연결. mafia.room.lock=redis 일 때만 만든다.
 *
 * 방 데이터(LobbyRoomRepository)가 쓰는 Spring 기본 Redis 설정(spring.data.redis.*)을 그대로 쓴다.
 * 방 잠금은 방 데이터와 함께 바뀌어야 하므로 같은 그룹에 둔다.
 * 기본 연결(Lettuce)은 건드리지 않고, 잠금용 Redisson 연결만 따로 만든다.
 *
 * 값이 local이거나 없으면 만들지 않는다. (Redisson은 만들 때 바로 Redis에 접속한다)
 * 이 연결을 쓰는 방 잠금 구현(RedisRoomLock)은 2-4에서 추가한다.
 */
@Configuration
@ConditionalOnProperty(name = "mafia.room.lock", havingValue = "redis")
public class RoomLockRedisConfig {

    @Bean(destroyMethod = "shutdown")
    public RedissonClient lobbyRedisson(@Value("${spring.data.redis.host:localhost}") String host,
                                        @Value("${spring.data.redis.port:6379}") int port,
                                        @Value("${spring.data.redis.database:0}") int database,
                                        @Value("${spring.data.redis.password:}") String password) {
        return RedissonClients.create(host, port, database, password);
    }
}
