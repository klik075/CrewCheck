package com.WhoisntCitizen_server.game.activity.redis;

import com.WhoisntCitizen_server.common.servermode.ConditionalOnServerSetting;
import com.WhoisntCitizen_server.common.servermode.ServerSetting;
import com.WhoisntCitizen_server.game.activity.PlayerActivityTracker;
import com.WhoisntCitizen_server.game.repository.redis.GameRedis;
import com.WhoisntCitizen_server.game.repository.redis.GameRedisProperties;
import org.springframework.context.annotation.Bean;
import org.springframework.context.annotation.Configuration;

import java.time.Duration;

/**
 * Redis 접속 기록(RedisPlayerActivityTracker). mafia.game.activity의 최종 값이 redis일 때 쓴다. (서버 여러 대)
 * 게임 그룹 Redis 연결(GameRedis)을 쓴다. 그 연결은 GameRedisConnectionConfig가 접속 기록이 redis일 때도 만들어 준다.
 * (OnGameRedisNeededCondition에 GAME_ACTIVITY가 들어 있다)
 */
@Configuration
public class PlayerActivityRedisConfig {

    /** 키 TTL은 진행 중인 게임 상태 키와 같은 값(mafia.redis.game.active-ttl-seconds, 기본 6시간)을 쓴다 */
    @Bean
    @ConditionalOnServerSetting(value = ServerSetting.GAME_ACTIVITY, havingValue = "redis")
    public PlayerActivityTracker playerActivityTracker(GameRedis gameRedis, GameRedisProperties props) {
        return new RedisPlayerActivityTracker(gameRedis, Duration.ofSeconds(props.activeTtlSeconds()));
    }
}
