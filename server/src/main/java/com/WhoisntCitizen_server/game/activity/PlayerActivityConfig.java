package com.WhoisntCitizen_server.game.activity;

import com.WhoisntCitizen_server.common.servermode.ConditionalOnServerSetting;
import com.WhoisntCitizen_server.common.servermode.ServerSetting;
import org.springframework.context.annotation.Bean;
import org.springframework.context.annotation.Configuration;

/**
 * 서버 메모리 접속 기록(LocalPlayerActivityTracker). mafia.game.activity의 최종 값이 local일 때 쓴다. (서버 1대 기준)
 * 최종 값: mafia.game.activity에 값이 있으면 그 값, 비어 있으면 mafia.server.mode를 따른다 (single → local). ServerSettings 참고
 * redis면 이 Bean은 만들지 않고 PlayerActivityRedisConfig의 RedisPlayerActivityTracker를 쓴다. 둘 중 하나만 등록된다.
 * (예전에는 GameConfig 안에 있었다. 잠금·타이머처럼 설정마다 따로 뺐다)
 */
@Configuration
public class PlayerActivityConfig {

    @Bean
    @ConditionalOnServerSetting(value = ServerSetting.GAME_ACTIVITY, havingValue = "local")
    public PlayerActivityTracker playerActivityTracker() {
        return new LocalPlayerActivityTracker();
    }
}
