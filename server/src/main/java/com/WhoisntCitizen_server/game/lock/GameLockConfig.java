package com.WhoisntCitizen_server.game.lock;

import com.WhoisntCitizen_server.common.servermode.ConditionalOnServerSetting;
import com.WhoisntCitizen_server.common.servermode.ServerSetting;
import org.springframework.beans.factory.annotation.Value;
import org.springframework.context.annotation.Bean;
import org.springframework.context.annotation.Configuration;

import java.time.Duration;

/**
 * 서버 메모리 게임 잠금(LocalGameLock). mafia.game.lock의 최종 값이 local일 때 쓴다. (서버 1대 기준)
 * 최종 값: mafia.game.lock에 값이 있으면 그 값, 비어 있으면 mafia.server.mode를 따른다 (single → local). ServerSettings 참고
 * redis면 이 Bean은 만들지 않고 GameLockRedisConfig의 RedisGameLock을 쓴다. 둘 중 하나만 등록된다.
 * (예전에는 GameConfig 안에 있었다. 방 잠금 RoomLockConfig와 같은 모양으로 따로 뺐다)
 */
@Configuration
public class GameLockConfig {

    /** 대기 시간(mafia.lock.wait-timeout-millis)을 넘기면 LockTimeoutException */
    @Bean
    @ConditionalOnServerSetting(value = ServerSetting.GAME_LOCK, havingValue = "local")
    public GameLock gameLock(@Value("${mafia.lock.wait-timeout-millis:10000}") long waitTimeoutMillis) {
        return new LocalGameLock(Duration.ofMillis(waitTimeoutMillis));
    }
}
