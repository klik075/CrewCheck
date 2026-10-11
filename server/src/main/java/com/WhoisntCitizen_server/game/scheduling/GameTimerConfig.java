package com.WhoisntCitizen_server.game.scheduling;

import com.WhoisntCitizen_server.common.servermode.ConditionalOnServerSetting;
import com.WhoisntCitizen_server.common.servermode.ServerSetting;
import org.springframework.beans.factory.ObjectProvider;
import org.springframework.beans.factory.annotation.Qualifier;
import org.springframework.context.annotation.Bean;
import org.springframework.context.annotation.Configuration;
import org.springframework.scheduling.TaskScheduler;

/**
 * 서버 메모리 게임 타이머(LocalGameTimer). mafia.game.timer의 최종 값이 local일 때 쓴다. (서버 1대 기준)
 * 최종 값: mafia.game.timer에 값이 있으면 그 값, 비어 있으면 mafia.server.mode를 따른다 (single → local). ServerSettings 참고
 * redis면 이 Bean은 만들지 않고 GameTimerRedisConfig의 RedisGameTimer를 쓴다. 둘 중 하나만 등록된다.
 * (예전에는 GameConfig 안에 있었다. 잠금처럼 설정마다 따로 뺐다)
 */
@Configuration
public class GameTimerConfig {

    /**
     * 페이즈 제한 시간·끝난 게임 정리를 서버 메모리의 스케줄러(gamePhaseScheduler)에 예약한다. 서버가 꺼지면 예약도 사라진다.
     * handler(GameFlowService)는 ObjectProvider로 호출 시점에 꺼낸다. GameFlowService도 GameTimer를 주입받으므로
     * 여기서 바로 주입받으면 순환 의존이 된다.
     */
    @Bean
    @ConditionalOnServerSetting(value = ServerSetting.GAME_TIMER, havingValue = "local")
    public GameTimer gameTimer(@Qualifier("gamePhaseScheduler") TaskScheduler scheduler,
                               ObjectProvider<GameTimeoutHandler> handler) {
        return new LocalGameTimer(scheduler, handler::getObject);
    }
}
