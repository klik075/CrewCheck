package com.sparta.unityaitestproject_server.common.config;

import org.springframework.boot.context.properties.EnableConfigurationProperties;
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

    /** 페이즈 타이머 전용 스케줄러. */
    @Bean
    public TaskScheduler gamePhaseScheduler() {
        ThreadPoolTaskScheduler scheduler = new ThreadPoolTaskScheduler();
        scheduler.setPoolSize(4);
        scheduler.setThreadNamePrefix("game-phase-");
        scheduler.initialize();
        return scheduler;
    }
}
