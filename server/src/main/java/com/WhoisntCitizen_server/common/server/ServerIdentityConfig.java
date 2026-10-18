package com.WhoisntCitizen_server.common.server;

import com.WhoisntCitizen_server.common.servermode.ConditionalOnServerSetting;
import com.WhoisntCitizen_server.common.servermode.ServerSetting;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;
import org.springframework.beans.factory.annotation.Qualifier;
import org.springframework.beans.factory.annotation.Value;
import org.springframework.context.annotation.Bean;
import org.springframework.context.annotation.Configuration;
import org.springframework.data.redis.core.StringRedisTemplate;
import org.springframework.scheduling.TaskScheduler;

/**
 * 서버 식별자 관련 Bean. (멀티서버 2단계)
 *  - ServerIdentity: 서버 모드와 상관없이 항상 등록한다. (single이어도 응답 헤더·로그에 쓴다)
 *  - ServerIdHeaderFilter: 모든 HTTP 응답에 X-Server-Id
 *  - ServerInstanceRegistry: 실시간 신호가 redis일 때만. 같은 ID를 쓰는 서버가 있는지 Redis로 확인
 */
@Configuration
public class ServerIdentityConfig {

    private static final Logger log = LoggerFactory.getLogger(ServerIdentityConfig.class);

    @Bean
    public ServerIdentity serverIdentity(@Value("${mafia.server.id:}") String configuredId) {
        ServerIdentity identity = ServerIdentity.resolve(configuredId);
        log.info("[server] 서버 ID: {}", identity);
        return identity;
    }

    /**
     * Filter 타입 Bean은 Spring Boot가 모든 주소(/*)에 자동으로 붙인다.
     * 순서는 필터가 직접 알려 준다(Ordered, 가장 앞). Spring Security가 401·403으로 일찍 끝내는 응답에도 헤더가 붙는다.
     */
    @Bean
    public ServerIdHeaderFilter serverIdHeaderFilter(ServerIdentity serverIdentity) {
        return new ServerIdHeaderFilter(serverIdentity);
    }

    @Bean
    @ConditionalOnServerSetting(value = ServerSetting.REALTIME_NOTIFIER, havingValue = "redis")
    public ServerInstanceRegistry serverInstanceRegistry(ServerIdentity serverIdentity, StringRedisTemplate redis,
                                                         @Qualifier("gamePhaseScheduler") TaskScheduler scheduler) {
        return new ServerInstanceRegistry(serverIdentity, redis, scheduler);
    }
}
