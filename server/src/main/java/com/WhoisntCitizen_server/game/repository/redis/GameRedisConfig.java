package com.WhoisntCitizen_server.game.repository.redis;

import com.WhoisntCitizen_server.game.repository.GameRepository;
import com.WhoisntCitizen_server.game.repository.snapshot.GameSnapshotCodec;
import org.springframework.boot.autoconfigure.condition.ConditionalOnProperty;
import org.springframework.boot.context.properties.EnableConfigurationProperties;
import org.springframework.context.annotation.Bean;
import org.springframework.context.annotation.Configuration;

import java.time.Clock;

/**
 * mafia.game.repository=redis 일 때 게임 상태를 Redis에 저장한다.
 * 게임 그룹 Redis 연결(GameRedis)과 RedisGameRepository를 등록한다.
 *
 * 값이 memory이거나 없으면 이 설정은 통째로 빠지고 InMemoryGameRepository가 쓰인다. (게임용 Redis 연결도 만들지 않는다)
 * 둘 중 하나만 등록되므로 GameRepository를 주입받는 서비스 코드는 어느 쪽인지 몰라도 된다.
 *
 * 주의: redis로 바꿔도 잠금(LocalGameLock)·타이머(LocalGameTimer)·접속 기록은 아직 서버 메모리에 있다.
 * 그래서 서버는 1대여야 한다. 여러 대는 2단계(분산 잠금), 3단계(Redis 타이머) 이후에 가능하다.
 */
@Configuration
@ConditionalOnProperty(name = "mafia.game.repository", havingValue = "redis")
@EnableConfigurationProperties(GameRedisProperties.class)
public class GameRedisConfig {

    @Bean
    public GameRedis gameRedis(GameRedisProperties props) {
        return new GameRedis(props);
    }

    @Bean
    public GameRepository redisGameRepository(GameRedis gameRedis, GameRedisProperties props, Clock clock) {
        return new RedisGameRepository(gameRedis, new GameSnapshotCodec(), props, clock);
    }
}
