package com.WhoisntCitizen_server.realtime.redis;

import com.WhoisntCitizen_server.common.servermode.ConditionalOnServerSetting;
import com.WhoisntCitizen_server.common.servermode.ServerSetting;
import com.WhoisntCitizen_server.realtime.LocalRealtimeNotifier;
import com.WhoisntCitizen_server.realtime.RealtimeSessionRegistry;
import com.WhoisntCitizen_server.realtime.protocol.RealtimeMessageCodec;
import jakarta.annotation.PreDestroy;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;
import org.springframework.beans.factory.annotation.Value;
import org.springframework.context.annotation.Bean;
import org.springframework.context.annotation.Configuration;
import org.springframework.data.redis.connection.RedisConnectionFactory;
import org.springframework.data.redis.core.StringRedisTemplate;
import org.springframework.data.redis.listener.ChannelTopic;
import org.springframework.data.redis.listener.RedisMessageListenerContainer;
import org.springframework.scheduling.concurrent.ThreadPoolTaskExecutor;

import java.net.InetAddress;
import java.util.UUID;
import java.util.concurrent.ThreadPoolExecutor;

/**
 * Redis Pub/Sub 실시간 신호(RedisRealtimeNotifier). mafia.realtime.notifier의 최종 값이 redis일 때 쓴다. (서버 여러 대)
 * 최종 값: mafia.realtime.notifier에 값이 있으면 그 값, 비어 있으면 mafia.server.mode를 따른다 (multi → redis). ServerSettings 참고
 * local이면 이 설정 전체를 건너뛰고 RealtimeWebSocketConfig의 LocalRealtimeNotifier를 쓴다. 둘 중 하나만 등록된다.
 *
 * 연결은 공용(로비 그룹) Redis(spring.data.redis.*)의 기본 연결을 쓴다. 신호는 게임·방·채팅 모두에서 나오므로 게임 그룹에 두지 않았다.
 * Pub/Sub은 키를 만들지 않는다. 메시지는 저장되지 않고 그 순간 구독 중인 서버에만 간다.
 *
 * 구성
 *  - RedisRealtimeNotifier: 다른 모듈이 쓰는 RealtimeNotifier. 자기 연결에 보내고 채널에 발행
 *  - RealtimeSignalListener: 채널에서 받은 다른 서버의 신호를 자기 연결에 반영
 *  - RedisMessageListenerContainer: 채널 구독을 맡는다. Redis 연결이 끊기면 다시 구독한다
 */
@Configuration
@ConditionalOnServerSetting(value = ServerSetting.REALTIME_NOTIFIER, havingValue = "redis")
public class RealtimeRedisConfig {

    private static final Logger log = LoggerFactory.getLogger(RealtimeRedisConfig.class);

    /**
     * 받은 신호를 처리하는 작업 대기열 크기. 넘치면 가장 오래된 신호를 버린다.
     * 신호가 조금 빠져도 클라이언트의 안전망 조회(15초)가 맞춰 주므로, 메모리를 무한히 쓰는 것보다 버리는 편이 낫다.
     */
    static final int SIGNAL_QUEUE_CAPACITY = 10_000;

    private ThreadPoolTaskExecutor signalExecutor;

    @Bean
    public RealtimeSignalCodec realtimeSignalCodec() {
        return new RealtimeSignalCodec();
    }

    /**
     * 다른 모듈(게임·로비·채팅·회원)이 주입받는 RealtimeNotifier.
     * 안의 LocalRealtimeNotifier는 Bean으로 따로 등록하지 않는다. RealtimeNotifier 타입 Bean이 둘이 되면 주입할 때 어느 것인지 모호해진다.
     */
    @Bean
    public RedisRealtimeNotifier redisRealtimeNotifier(RealtimeSessionRegistry registry, RealtimeMessageCodec messageCodec,
                                                       StringRedisTemplate redis,
                                                       @Value("${mafia.realtime.redis.channel:realtime:v1:signals}") String channel) {
        String serverId = newServerId();
        log.info("[ws] 실시간 신호: Redis Pub/Sub (channel={}, serverId={})", channel, serverId);
        return new RedisRealtimeNotifier(new LocalRealtimeNotifier(registry, messageCodec), messageCodec,
                realtimeSignalCodec(), redis, channel, serverId);
    }

    @Bean
    public RealtimeSignalListener realtimeSignalListener(RedisRealtimeNotifier redisRealtimeNotifier) {
        return new RealtimeSignalListener(redisRealtimeNotifier, realtimeSignalCodec());
    }

    /**
     * 채널을 구독하는 컨테이너. 서버가 켜질 때 구독을 시작하고(SmartLifecycle), 꺼질 때 구독을 푼다.
     *
     * 받은 신호는 작업 스레드 하나가 차례대로 처리한다.
     * 기본값(SimpleAsyncTaskExecutor)은 메시지마다 새 스레드를 만들어 순서가 섞인다.
     * 예) 같은 서버가 보낸 "철수를 3번 방 구독에서 빼라" → "3번 방 바뀜"이 뒤집혀 처리되면 이미 나간 철수가 신호를 하나 더 받는다.
     */
    @Bean
    public RedisMessageListenerContainer realtimeSignalListenerContainer(RedisConnectionFactory connectionFactory,
                                                                         RealtimeSignalListener realtimeSignalListener,
                                                                         RedisRealtimeNotifier redisRealtimeNotifier) {
        signalExecutor = new ThreadPoolTaskExecutor();
        signalExecutor.setCorePoolSize(1);
        signalExecutor.setMaxPoolSize(1);
        signalExecutor.setQueueCapacity(SIGNAL_QUEUE_CAPACITY);
        signalExecutor.setThreadNamePrefix("realtime-signal-");
        signalExecutor.setRejectedExecutionHandler(new ThreadPoolExecutor.DiscardOldestPolicy());
        signalExecutor.initialize();

        RedisMessageListenerContainer container = new RedisMessageListenerContainer();
        container.setConnectionFactory(connectionFactory);
        container.setTaskExecutor(signalExecutor);
        container.addMessageListener(realtimeSignalListener, ChannelTopic.of(redisRealtimeNotifier.channel()));
        return container;
    }

    /** 컨테이너가 구독을 푼 뒤(SmartLifecycle stop) 작업 스레드를 정리한다 */
    @PreDestroy
    void shutdownSignalExecutor() {
        if (signalExecutor != null) {
            signalExecutor.shutdown();
        }
    }

    /**
     * 이 서버 프로세스의 ID. 켜질 때마다 새로 만든다. (호스트 이름 + 임의 8자리)
     * 같은 컴퓨터에서 서버 두 대를 띄워도, 설정을 잘못 복사해도 겹치지 않게 임의 값을 붙인다.
     * 2단계(서버 식별자·헬스 체크)에서 서버 ID를 따로 만들면 그 값으로 바꾼다.
     */
    static String newServerId() {
        String host;
        try {
            host = InetAddress.getLocalHost().getHostName();
        } catch (Exception e) {
            host = "server";
        }
        return host + "-" + UUID.randomUUID().toString().substring(0, 8);
    }
}
