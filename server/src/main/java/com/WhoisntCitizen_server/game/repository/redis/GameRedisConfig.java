package com.WhoisntCitizen_server.game.repository.redis;

import com.WhoisntCitizen_server.common.servermode.ConditionalOnServerSetting;
import com.WhoisntCitizen_server.common.servermode.ServerSetting;
import com.WhoisntCitizen_server.game.repository.GameRepository;
import com.WhoisntCitizen_server.game.repository.snapshot.GameSnapshotCodec;
import org.springframework.boot.context.properties.EnableConfigurationProperties;
import org.springframework.context.annotation.Bean;
import org.springframework.context.annotation.Configuration;

import java.time.Clock;

/**
 * mafia.game.repository의 최종 값이 redis일 때 게임 상태를 Redis에 저장한다.
 * (직접 지정, 또는 비워 두고 mafia.server.mode=multi)
 * RedisGameRepository를 등록한다. 게임 그룹 Redis 연결(GameRedis)은 GameRedisConnectionConfig가 만든다.
 *
 * 최종 값이 memory면 이 설정은 통째로 빠지고 InMemoryGameRepository가 쓰인다.
 * 둘 중 하나만 등록되므로 GameRepository를 주입받는 서비스 코드는 어느 쪽인지 몰라도 된다.
 *
 * 주의: 접속 기록은 아직 서버 메모리에 있다. 서버 여러 대는 3.5단계(접속 기록) 이후에 가능하다. (ServerSettingsCheck 경고 참고)
 */
@Configuration
@ConditionalOnServerSetting(value = ServerSetting.GAME_REPOSITORY, havingValue = "redis")
@EnableConfigurationProperties(GameRedisProperties.class)
public class GameRedisConfig {

    @Bean
    public GameRepository redisGameRepository(GameRedis gameRedis, GameRedisProperties props, Clock clock) {
        return new RedisGameRepository(gameRedis, new GameSnapshotCodec(), props, clock);
    }
}
