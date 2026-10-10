package com.WhoisntCitizen_server.game.lock.redis;

import com.WhoisntCitizen_server.common.redis.RedissonClients;
import com.WhoisntCitizen_server.common.servermode.ConditionalOnServerSetting;
import com.WhoisntCitizen_server.common.servermode.ServerSetting;
import com.WhoisntCitizen_server.game.lock.GameLock;
import com.WhoisntCitizen_server.game.repository.redis.GameRedisProperties;
import org.redisson.api.RedissonClient;
import org.springframework.beans.factory.annotation.Qualifier;
import org.springframework.beans.factory.annotation.Value;
import org.springframework.boot.context.properties.EnableConfigurationProperties;
import org.springframework.context.annotation.Bean;
import org.springframework.context.annotation.Configuration;

import java.time.Duration;

/**
 * 게임 그룹 Redisson 연결. mafia.game.lock의 최종 값이 redis일 때만 만든다.
 * (mafia.game.lock=redis로 직접 지정했거나, 비워 두고 mafia.server.mode=multi인 경우)
 *
 * 게임 상태(GameRedis)와 같은 설정(mafia.redis.game.*)을 쓴다. 게임 잠금은 게임 상태와 함께 바뀌어야 하므로
 * 게임용 Redis를 따로 두면(GAME_REDIS_HOST) 게임 상태와 게임 잠금이 함께 옮겨간다.
 *
 * 최종 값이 local이면 만들지 않는다. Redisson은 만들 때 바로 Redis에 접속하므로,
 * 쓰지 않는데 만들면 Redis 없이 서버를 띄울 수 없게 된다.
 * 이 연결로 게임 잠금(RedisGameLock)을 만들어 GameLock Bean으로 등록한다.
 * 최종 값이 local이면 GameLockConfig의 LocalGameLock이 쓰인다. 둘 중 하나만 등록된다.
 */
@Configuration
@ConditionalOnServerSetting(value = ServerSetting.GAME_LOCK, havingValue = "redis")
@EnableConfigurationProperties(GameRedisProperties.class)
public class GameLockRedisConfig {

    @Bean(destroyMethod = "shutdown")
    public RedissonClient gameRedisson(GameRedisProperties props) {
        return RedissonClients.create(props.host(), props.port(), props.database(), props.password());
    }

    /** 서버 여러 대가 함께 쓰는 게임 잠금. 대기 시간(mafia.lock.wait-timeout-millis)을 넘기면 LockTimeoutException */
    @Bean
    public GameLock redisGameLock(@Qualifier("gameRedisson") RedissonClient gameRedisson,
                                  @Value("${mafia.lock.wait-timeout-millis:10000}") long waitTimeoutMillis) {
        return new RedisGameLock(gameRedisson, Duration.ofMillis(waitTimeoutMillis));
    }
}
