package com.WhoisntCitizen_server.game.lock;

import org.springframework.beans.factory.annotation.Value;
import org.springframework.boot.autoconfigure.condition.ConditionalOnProperty;
import org.springframework.context.annotation.Bean;
import org.springframework.context.annotation.Configuration;

import java.time.Duration;

/**
 * 서버 메모리 게임 잠금(LocalGameLock). mafia.game.lock=local 이거나 값이 없을 때 쓴다. (서버 1대 기준)
 * redis면 이 Bean은 만들지 않고 GameLockRedisConfig의 RedisGameLock을 쓴다. 둘 중 하나만 등록된다.
 * (예전에는 GameConfig 안에 있었다. 방 잠금 RoomLockConfig와 같은 모양으로 따로 뺐다)
 */
@Configuration
public class GameLockConfig {

    /** 대기 시간(mafia.lock.wait-timeout-millis)을 넘기면 LockTimeoutException */
    @Bean
    @ConditionalOnProperty(name = "mafia.game.lock", havingValue = "local", matchIfMissing = true)
    public GameLock gameLock(@Value("${mafia.lock.wait-timeout-millis:10000}") long waitTimeoutMillis) {
        return new LocalGameLock(Duration.ofMillis(waitTimeoutMillis));
    }
}
