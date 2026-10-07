package com.WhoisntCitizen_server.common.config;

import com.WhoisntCitizen_server.game.scheduling.DeferredEventPublisher;
import com.WhoisntCitizen_server.game.scheduling.GameTimeoutHandler;
import com.WhoisntCitizen_server.game.scheduling.GameTimer;
import com.WhoisntCitizen_server.game.scheduling.LocalGameTimer;
import com.WhoisntCitizen_server.game.scheduling.SchedulerDeferredEventPublisher;
import org.springframework.beans.factory.ObjectProvider;
import org.springframework.beans.factory.annotation.Qualifier;
import org.springframework.boot.context.properties.EnableConfigurationProperties;
import org.springframework.context.ApplicationEventPublisher;
import org.springframework.context.annotation.Bean;
import org.springframework.context.annotation.Configuration;
import org.springframework.scheduling.TaskScheduler;
import org.springframework.scheduling.concurrent.ThreadPoolTaskScheduler;

import java.security.SecureRandom;
import java.time.Clock;
import java.util.Random;

@Configuration
@EnableConfigurationProperties(GamePhaseProperties.class)
public class GameConfig {

    /** 역할 배정/동률 처리용. 테스트에서는 시드 고정 Random으로 교체 가능. */
    @Bean
    public Random gameRandom() {
        return new SecureRandom();
    }

    @Bean
    public Clock clock() {
        return Clock.systemUTC();
    }

    /**
     * 페이즈 타이머 전용 스케줄러.
     * 예약 시각(Instant)을 지연 시간으로 바꿀 때 phaseEndsAt과 같은 Clock을 쓰도록 맞춘다.
     * (기본값은 스케줄러 자체의 Clock.systemDefaultZone())
     */
    @Bean
    public TaskScheduler gamePhaseScheduler(Clock clock) {
        ThreadPoolTaskScheduler scheduler = new ThreadPoolTaskScheduler();
        scheduler.setPoolSize(4);
        scheduler.setThreadNamePrefix("game-phase-");
        scheduler.setClock(clock);
        //scheduler.initialize();
        return scheduler;
    }

    /**
     * 게임 타이머(페이즈 제한 시간, 끝난 게임 정리). 지금은 서버 메모리의 스케줄러에 예약한다.
     * handler(GameFlowService)는 ObjectProvider로 호출 시점에 꺼낸다. GameFlowService도 GameTimer를 주입받으므로
     * 여기서 바로 주입받으면 순환 의존이 된다.
     */
    @Bean
    public GameTimer gameTimer(@Qualifier("gamePhaseScheduler") TaskScheduler scheduler,
                               ObjectProvider<GameTimeoutHandler> handler) {
        return new LocalGameTimer(scheduler, handler::getObject);
    }

    /** 게임 잠금이 풀린 뒤 발행할 이벤트(게임 종료, 연결 끊김). 스케줄러 스레드에서 발행한다. */
    @Bean
    public DeferredEventPublisher deferredEventPublisher(@Qualifier("gamePhaseScheduler") TaskScheduler scheduler,
                                                         Clock clock,
                                                         ApplicationEventPublisher eventPublisher) {
        return new SchedulerDeferredEventPublisher(scheduler, clock, eventPublisher);
    }
}
