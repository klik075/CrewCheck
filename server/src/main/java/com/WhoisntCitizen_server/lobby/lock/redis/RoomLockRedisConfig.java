package com.WhoisntCitizen_server.lobby.lock.redis;

import com.WhoisntCitizen_server.common.redis.RedissonClients;
import com.WhoisntCitizen_server.common.servermode.ConditionalOnServerSetting;
import com.WhoisntCitizen_server.common.servermode.ServerSetting;
import com.WhoisntCitizen_server.lobby.lock.RoomLock;
import org.redisson.api.RedissonClient;
import org.springframework.beans.factory.annotation.Qualifier;
import org.springframework.beans.factory.annotation.Value;
import org.springframework.context.annotation.Bean;
import org.springframework.context.annotation.Configuration;

import java.time.Duration;

/**
 * 로비 그룹 Redisson 연결. mafia.room.lock의 최종 값이 redis일 때만 만든다.
 * (mafia.room.lock=redis로 직접 지정했거나, 비워 두고 mafia.server.mode=multi인 경우)
 *
 * 방 데이터(LobbyRoomRepository)가 쓰는 Spring 기본 Redis 설정(spring.data.redis.*)을 그대로 쓴다.
 * 방 잠금은 방 데이터와 함께 바뀌어야 하므로 같은 그룹에 둔다.
 * 기본 연결(Lettuce)은 건드리지 않고, 잠금용 Redisson 연결만 따로 만든다.
 *
 * 최종 값이 local이면 만들지 않는다. (Redisson은 만들 때 바로 Redis에 접속한다)
 * 이 연결로 방 잠금(RedisRoomLock)을 만들어 RoomLock Bean으로 등록한다.
 * 최종 값이 local이면 RoomLockConfig의 LocalRoomLock이 쓰인다. 둘 중 하나만 등록된다.
 */
@Configuration
@ConditionalOnServerSetting(value = ServerSetting.ROOM_LOCK, havingValue = "redis")
public class RoomLockRedisConfig {

    @Bean(destroyMethod = "shutdown")
    public RedissonClient lobbyRedisson(@Value("${spring.data.redis.host:localhost}") String host,
                                        @Value("${spring.data.redis.port:6379}") int port,
                                        @Value("${spring.data.redis.database:0}") int database,
                                        @Value("${spring.data.redis.password:}") String password) {
        return RedissonClients.create(host, port, database, password);
    }

    /** 서버 여러 대가 함께 쓰는 방 잠금. 대기 시간(mafia.lock.wait-timeout-millis)을 넘기면 LockTimeoutException */
    @Bean
    public RoomLock redisRoomLock(@Qualifier("lobbyRedisson") RedissonClient lobbyRedisson,
                                  @Value("${mafia.lock.wait-timeout-millis:10000}") long waitTimeoutMillis) {
        return new RedisRoomLock(lobbyRedisson, Duration.ofMillis(waitTimeoutMillis));
    }
}
