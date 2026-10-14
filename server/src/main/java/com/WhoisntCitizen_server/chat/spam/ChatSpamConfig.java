package com.WhoisntCitizen_server.chat.spam;

import org.slf4j.Logger;
import org.slf4j.LoggerFactory;
import org.springframework.beans.factory.ObjectProvider;
import org.springframework.beans.factory.annotation.Value;
import org.springframework.context.annotation.Bean;
import org.springframework.context.annotation.Configuration;
import org.springframework.data.redis.core.StringRedisTemplate;

import java.time.Duration;

/**
 * 도배 방지 빈. (application.properties)
 *  - chat.spam.enabled                (기본 true)   false면 아무것도 막지 않는다
 *  - chat.spam.warn-at                (기본 3)      같은 내용 몇 번째부터 경고할지
 *  - chat.spam.mute-at                (기본 10)     같은 내용 몇 번째에 채팅을 금지할지 (그 메시지는 저장하지 않음)
 *  - chat.spam.mute-seconds           (기본 180)    채팅 금지 시간(초)
 *  - chat.spam.repeat-window-seconds  (기본 60)     같은 내용 사이가 이보다 길면 1부터 다시 센다
 *  - chat.spam.store                  (기본 redis)  redis(채팅과 같은 로비 Redis, 서버 여러 대 공유) 또는 memory(서버 메모리)
 */
@Configuration
public class ChatSpamConfig {

    private static final Logger log = LoggerFactory.getLogger(ChatSpamConfig.class);

    @Bean
    public ChatSpamGuard chatSpamGuard(
            @Value("${chat.spam.enabled:true}") boolean enabled,
            @Value("${chat.spam.warn-at:3}") int warnAt,
            @Value("${chat.spam.mute-at:10}") int muteAt,
            @Value("${chat.spam.mute-seconds:180}") long muteSeconds,
            @Value("${chat.spam.repeat-window-seconds:60}") long repeatWindowSeconds,
            @Value("${chat.spam.store:redis}") String storeType,
            ObjectProvider<StringRedisTemplate> redis) {
        if (!enabled) {
            log.info("[Chat] 도배 방지 꺼짐 (chat.spam.enabled=false)");
            return ChatSpamGuard.disabled();
        }
        ChatSpamPolicy policy = new ChatSpamPolicy(warnAt, muteAt,
                Duration.ofSeconds(muteSeconds), Duration.ofSeconds(repeatWindowSeconds));

        ChatSpamStore store;
        StringRedisTemplate template = redis.getIfAvailable();
        if ("memory".equalsIgnoreCase(storeType.trim()) || template == null) {
            store = new InMemoryChatSpamStore();
        } else {
            store = new RedisChatSpamStore(template);
        }
        ChatSpamGuard guard = ChatSpamGuard.of(store, policy);
        log.info("[Chat] 도배 방지 켜짐: {}", guard);
        return guard;
    }
}
