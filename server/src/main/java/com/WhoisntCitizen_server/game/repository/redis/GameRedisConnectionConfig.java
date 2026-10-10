package com.WhoisntCitizen_server.game.repository.redis;

import org.springframework.boot.context.properties.EnableConfigurationProperties;
import org.springframework.context.annotation.Bean;
import org.springframework.context.annotation.Conditional;
import org.springframework.context.annotation.Configuration;

/**
 * 게임 그룹 Redis 연결(GameRedis). 게임 상태 저장소·게임 타이머·접속 기록 중 하나라도 redis일 때 하나만 만든다.
 * (OnGameRedisNeededCondition)
 *
 * 예전에는 GameRedisConfig(저장소) 안에 있었다. 게임 타이머도 같은 연결을 쓰게 되면서 따로 뺐다.
 * 저장소는 memory인데 타이머만 redis여도(또는 그 반대) 연결이 하나만 만들어지고, 둘 다 memory면 만들지 않는다.
 * 접속은 처음 명령을 보낼 때 맺는다. (GameRedis 참고)
 */
@Configuration
@Conditional(OnGameRedisNeededCondition.class)
@EnableConfigurationProperties(GameRedisProperties.class)
public class GameRedisConnectionConfig {

    @Bean
    public GameRedis gameRedis(GameRedisProperties props) {
        return new GameRedis(props);
    }
}
