package com.WhoisntCitizen_server.lobby.config;

import com.WhoisntCitizen_server.lobby.lock.LocalRoomLock;
import com.WhoisntCitizen_server.lobby.lock.RoomLock;
import org.springframework.beans.factory.annotation.Value;
import org.springframework.context.annotation.Bean;
import org.springframework.context.annotation.Configuration;

import java.time.Duration;

/** 방 잠금 구현 선택. 서버를 여러 대로 늘릴 때 여기서 Redis 분산 락 구현으로 바꾼다. */
@Configuration
public class RoomLockConfig {

    /** 대기 시간(mafia.lock.wait-timeout-millis)을 넘기면 LockTimeoutException */
    @Bean
    public RoomLock roomLock(@Value("${mafia.lock.wait-timeout-millis:10000}") long waitTimeoutMillis) {
        return new LocalRoomLock(Duration.ofMillis(waitTimeoutMillis));
    }
}
