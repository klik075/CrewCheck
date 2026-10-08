package com.WhoisntCitizen_server.lobby.config;

import com.WhoisntCitizen_server.lobby.lock.LocalRoomLock;
import com.WhoisntCitizen_server.lobby.lock.RoomLock;
import org.springframework.context.annotation.Bean;
import org.springframework.context.annotation.Configuration;

/** 방 잠금 구현 선택. 서버를 여러 대로 늘릴 때 여기서 Redis 분산 락 구현으로 바꾼다. */
@Configuration
public class RoomLockConfig {

    @Bean
    public RoomLock roomLock() { return new LocalRoomLock(); }
}
