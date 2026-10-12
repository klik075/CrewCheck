package com.WhoisntCitizen_server.chat.filter;

import org.slf4j.Logger;
import org.slf4j.LoggerFactory;
import org.springframework.beans.factory.annotation.Value;
import org.springframework.context.annotation.Bean;
import org.springframework.context.annotation.Configuration;
import org.springframework.core.io.Resource;

import java.io.IOException;
import java.io.InputStream;
import java.io.UncheckedIOException;
import java.util.List;

/**
 * 욕설 검열 필터 빈.
 *  - chat.profanity.enabled       (기본 true)  false면 아무것도 바꾸지 않는다
 *  - chat.profanity.banned-words  (기본 classpath:profanity/banned-words.txt)  금칙어 사전
 *  - chat.profanity.allowed-words (기본 classpath:profanity/allowed-words.txt) 허용어 사전 (금칙어를 품은 정상 단어)
 * 사전 위치에 file:/경로/banned-words.txt 를 주면 jar 밖의 파일을 쓸 수 있다. (사전을 바꾼 뒤 서버 재시작 필요)
 */
@Configuration
public class ProfanityFilterConfig {

    private static final Logger log = LoggerFactory.getLogger(ProfanityFilterConfig.class);

    @Bean
    public ProfanityFilter profanityFilter(
            @Value("${chat.profanity.enabled:true}") boolean enabled,
            @Value("${chat.profanity.banned-words:classpath:profanity/banned-words.txt}") Resource bannedWords,
            @Value("${chat.profanity.allowed-words:classpath:profanity/allowed-words.txt}") Resource allowedWords) {
        if (!enabled) {
            log.info("[Chat] 욕설 검열 꺼짐 (chat.profanity.enabled=false)");
            return ProfanityFilter.disabled();
        }
        List<String> banned = read(bannedWords, true);
        List<String> allowed = read(allowedWords, false);
        log.info("[Chat] 욕설 검열 켜짐: 금칙어 {}개, 허용어 {}개", banned.size(), allowed.size());
        return ProfanityFilter.of(banned, allowed);
    }

    private static List<String> read(Resource resource, boolean required) {
        if (!resource.exists()) {
            if (required) {
                throw new IllegalStateException("욕설 사전을 찾을 수 없습니다: " + resource.getDescription());
            }
            return List.of();
        }
        try (InputStream in = resource.getInputStream()) {
            return ProfanityFilter.readWords(in);
        } catch (IOException e) {
            throw new UncheckedIOException("욕설 사전을 읽지 못했습니다: " + resource.getDescription(), e);
        }
    }
}
