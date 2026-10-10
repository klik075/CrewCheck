package com.WhoisntCitizen_server.game.scheduling.redis;

import com.WhoisntCitizen_server.common.servermode.ConditionalOnServerSetting;
import com.WhoisntCitizen_server.common.servermode.ServerSetting;
import com.WhoisntCitizen_server.game.repository.redis.GameRedis;
import com.WhoisntCitizen_server.game.scheduling.GameTimeoutHandler;
import org.springframework.beans.factory.ObjectProvider;
import org.springframework.beans.factory.annotation.Qualifier;
import org.springframework.boot.context.properties.EnableConfigurationProperties;
import org.springframework.context.annotation.Bean;
import org.springframework.context.annotation.Configuration;
import org.springframework.scheduling.TaskScheduler;

import java.time.Clock;

/**
 * Redis 게임 타이머. mafia.game.timer의 최종 값이 redis일 때만 만든다.
 * (mafia.game.timer=redis로 직접 지정했거나, 비워 두고 mafia.server.mode=multi인 경우)
 * 최종 값이 local이면 GameTimerConfig의 LocalGameTimer가 쓰인다. 둘 중 하나만 등록된다.
 *
 * 등록하는 것 (설정: mafia.redis.game.timer.*, RedisTimerProperties)
 *  - RedisGameTimer      : GameTimer. 예약을 game:v1:timers(게임 그룹 Redis)에 보관하고, 가져가기·실행·완료를 한다
 *  - RedisTimerDispatcher: 가져간 예약을 작업 스레드(game-timer-N, workers개)에 하나씩 넘긴다. 서버가 꺼질 때 close
 *  - RedisTimerPoller    : poll-interval-millis마다 dispatcher를 부른다. 공용 스케줄러(gamePhaseScheduler)에서 돈다.
 *                          SmartLifecycle이라 다른 Bean이 모두 준비된 뒤 시작하고, 꺼질 때 먼저 멈춘다
 *
 * 게임 그룹 Redis 연결(GameRedis)은 GameRedisConnectionConfig가 만든다. (게임 상태 저장소와 같은 연결)
 * 게임 상태는 Redis에 두는 것을 전제로 한다. 저장소가 memory인데 타이머만 redis면, 다른 서버가 가져간 타이머가
 * 자기 메모리에 없는 게임을 찾지 못한다. (서버 1대에서는 동작한다. ServerSettingsCheck가 섞인 조합을 경고한다)
 */
@Configuration
@ConditionalOnServerSetting(value = ServerSetting.GAME_TIMER, havingValue = "redis")
@EnableConfigurationProperties(RedisTimerProperties.class)
public class GameTimerRedisConfig {

    /**
     * handler(GameFlowService)는 ObjectProvider로 실행 시점에 꺼낸다. GameFlowService도 GameTimer를 주입받으므로
     * 여기서 바로 주입받으면 순환 의존이 된다. (LocalGameTimer와 같다)
     */
    @Bean
    public RedisGameTimer redisGameTimer(GameRedis gameRedis, ObjectProvider<GameTimeoutHandler> handler,
                                         Clock clock, RedisTimerProperties props) {
        return new RedisGameTimer(gameRedis, handler::getObject, clock, props.lease(), props.batchSize());
    }

    @Bean(destroyMethod = "close")
    public RedisTimerDispatcher redisTimerDispatcher(RedisGameTimer redisGameTimer, RedisTimerProperties props) {
        return new RedisTimerDispatcher(redisGameTimer, props.workers());
    }

    @Bean
    public RedisTimerPoller redisTimerPoller(RedisTimerDispatcher redisTimerDispatcher, RedisTimerProperties props,
                                             @Qualifier("gamePhaseScheduler") TaskScheduler scheduler, Clock clock) {
        return RedisTimerPoller.of(redisTimerDispatcher, props, scheduler, clock);
    }
}
