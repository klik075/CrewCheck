package com.WhoisntCitizen_server.game.repository;

import com.WhoisntCitizen_server.game.repository.redis.GameRedis;
import com.WhoisntCitizen_server.game.repository.redis.GameRedisConfig;
import com.WhoisntCitizen_server.game.repository.redis.GameRedisConnectionConfig;
import com.WhoisntCitizen_server.game.repository.redis.RedisGameRepository;
import org.junit.jupiter.api.Test;
import org.springframework.boot.test.context.runner.ApplicationContextRunner;

import java.time.Clock;

import static org.assertj.core.api.Assertions.assertThat;

/**
 * 1-9: mafia.game.repository 값에 따라 GameRepository가 하나만 등록되는지 확인한다.
 * Redis에 실제로 접속하지는 않는다. (GameRedis는 처음 명령을 보낼 때 접속한다)
 */
class GameRepositorySelectionTest {

    private final ApplicationContextRunner runner = new ApplicationContextRunner()
            .withUserConfiguration(InMemoryGameRepository.class, GameRedisConfig.class, GameRedisConnectionConfig.class)
            .withBean(Clock.class, Clock::systemUTC);

    @Test
    void 값이_없으면_메모리_저장소를_쓴다() {
        runner.run(context -> {
            assertThat(context).hasSingleBean(GameRepository.class);
            assertThat(context.getBean(GameRepository.class)).isInstanceOf(InMemoryGameRepository.class);
            assertThat(context).doesNotHaveBean(GameRedis.class);
        });
    }

    @Test
    void memory면_메모리_저장소를_쓰고_게임용_Redis_연결을_만들지_않는다() {
        runner.withPropertyValues("mafia.game.repository=memory").run(context -> {
            assertThat(context).hasSingleBean(GameRepository.class);
            assertThat(context.getBean(GameRepository.class)).isInstanceOf(InMemoryGameRepository.class);
            assertThat(context).doesNotHaveBean(GameRedis.class);
        });
    }

    @Test
    void redis면_Redis_저장소를_쓴다() {
        runner.withPropertyValues("mafia.game.repository=redis").run(context -> {
            assertThat(context).hasSingleBean(GameRepository.class);
            assertThat(context.getBean(GameRepository.class)).isInstanceOf(RedisGameRepository.class);
            assertThat(context).hasSingleBean(GameRedis.class);
        });
    }
}
