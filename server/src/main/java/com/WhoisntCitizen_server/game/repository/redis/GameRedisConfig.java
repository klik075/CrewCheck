package com.WhoisntCitizen_server.game.repository.redis;

import org.springframework.boot.context.properties.EnableConfigurationProperties;
import org.springframework.context.annotation.Bean;
import org.springframework.context.annotation.Configuration;

/**
 * 게임 그룹 Redis 연결을 등록한다.
 * RedisGameRepository를 GameRepository로 쓸지는 1-9에서 설정값(mafia.game.repository)으로 고른다.
 * 그 전까지는 연결 Bean만 있고 게임 저장은 InMemoryGameRepository가 한다.
 */
@Configuration
@EnableConfigurationProperties(GameRedisProperties.class)
public class GameRedisConfig {

    @Bean
    public GameRedis gameRedis(GameRedisProperties props) {
        return new GameRedis(props);
    }
}
