package com.WhoisntCitizen_server.lobby.config;

import com.WhoisntCitizen_server.lobby.lock.LocalRoomLock;
import com.WhoisntCitizen_server.lobby.lock.RoomLock;
import org.springframework.beans.factory.annotation.Value;
import org.springframework.boot.autoconfigure.condition.ConditionalOnProperty;
import org.springframework.context.annotation.Bean;
import org.springframework.context.annotation.Configuration;

import java.time.Duration;

/**
 * 서버 메모리 방 잠금(LocalRoomLock). mafia.room.lock=local 이거나 값이 없을 때 쓴다. (서버 1대 기준)
 * redis면 이 Bean은 만들지 않고 RoomLockRedisConfig의 RedisRoomLock을 쓴다. 둘 중 하나만 등록된다.
 */
@Configuration
public class RoomLockConfig {

    /** 대기 시간(mafia.lock.wait-timeout-millis)을 넘기면 LockTimeoutException */
    @Bean
    @ConditionalOnProperty(name = "mafia.room.lock", havingValue = "local", matchIfMissing = true)
    public RoomLock roomLock(@Value("${mafia.lock.wait-timeout-millis:10000}") long waitTimeoutMillis) {
        return new LocalRoomLock(Duration.ofMillis(waitTimeoutMillis));
    }
}
