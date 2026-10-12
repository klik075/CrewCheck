package com.WhoisntCitizen_server.game.repository;

import org.springframework.beans.factory.config.BeanPostProcessor;
import org.springframework.context.ApplicationEventPublisher;
import org.springframework.context.annotation.Bean;
import org.springframework.context.annotation.Configuration;

/**
 * 등록된 게임 저장소(memory 또는 redis)를 ChangeEventGameRepository로 감싼다.
 *
 * 저장소는 설정(mafia.game.repository)에 따라 InMemoryGameRepository 또는 RedisGameRepository 중 하나가 등록된다.
 * 두 곳을 각각 고치지 않고, 어느 쪽이 등록되든 만들어진 직후 감싸도록 BeanPostProcessor를 쓴다.
 * 서비스 코드는 GameRepository 인터페이스로만 주입받으므로 감싼 것을 그대로 쓴다.
 *
 * BeanPostProcessor는 다른 Bean보다 먼저 만들어져야 해서 static 메서드로 등록한다.
 */
@Configuration
public class GameChangeEventConfig {

    @Bean
    public static BeanPostProcessor gameChangeEventRepositoryWrapper(ApplicationEventPublisher eventPublisher) {
        return new BeanPostProcessor() {
            @Override
            public Object postProcessAfterInitialization(Object bean, String beanName) {
                if (bean instanceof GameRepository repository && !(bean instanceof ChangeEventGameRepository)) {
                    return new ChangeEventGameRepository(repository, eventPublisher);
                }
                return bean;
            }
        };
    }
}
