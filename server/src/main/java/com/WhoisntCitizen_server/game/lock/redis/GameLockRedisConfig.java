package com.WhoisntCitizen_server.game.lock.redis;

import com.WhoisntCitizen_server.common.redis.RedissonClients;
import com.WhoisntCitizen_server.game.repository.redis.GameRedisProperties;
import org.redisson.api.RedissonClient;
import org.springframework.boot.autoconfigure.condition.ConditionalOnProperty;
import org.springframework.boot.context.properties.EnableConfigurationProperties;
import org.springframework.context.annotation.Bean;
import org.springframework.context.annotation.Configuration;

/**
 * 게임 그룹 Redisson 연결. mafia.game.lock=redis 일 때만 만든다.
 *
 * 게임 상태(GameRedis)와 같은 설정(mafia.redis.game.*)을 쓴다. 게임 잠금은 게임 상태와 함께 바뀌어야 하므로
 * 게임용 Redis를 따로 두면(GAME_REDIS_HOST) 게임 상태와 게임 잠금이 함께 옮겨간다.
 *
 * 값이 local이거나 없으면 만들지 않는다. Redisson은 만들 때 바로 Redis에 접속하므로,
 * 쓰지 않는데 만들면 Redis 없이 서버를 띄울 수 없게 된다.
 * 이 연결로 만드는 게임 잠금 구현은 RedisGameLock이다. GameLock Bean으로 연결하는 것은 2-7에서 한다.
 */
@Configuration
@ConditionalOnProperty(name = "mafia.game.lock", havingValue = "redis")
@EnableConfigurationProperties(GameRedisProperties.class)
public class GameLockRedisConfig {

    @Bean(destroyMethod = "shutdown")
    public RedissonClient gameRedisson(GameRedisProperties props) {
        return RedissonClients.create(props.host(), props.port(), props.database(), props.password());
    }
}
