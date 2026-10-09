package com.WhoisntCitizen_server.game.repository.redis;

import org.springframework.beans.factory.DisposableBean;
import org.springframework.data.redis.connection.RedisPassword;
import org.springframework.data.redis.connection.RedisStandaloneConfiguration;
import org.springframework.data.redis.connection.lettuce.LettuceConnectionFactory;
import org.springframework.data.redis.core.StringRedisTemplate;

/**
 * 게임 그룹 Redis 연결. 게임 상태(1단계), 게임 잠금(2단계), 게임 타이머(3단계)가 이 연결을 쓴다.
 *
 * RedisConnectionFactory·StringRedisTemplate을 Bean으로 바로 등록하지 않고 이 클래스로 감싸는 이유:
 * Spring Boot는 그 타입의 Bean이 이미 있으면 기본 Redis 연결(spring.data.redis.*)을 만들지 않는다.
 * 그러면 로비·채팅이 쓰는 기본 연결이 사라지거나 어느 연결을 주입할지 모호해진다.
 * 이 타입으로 감싸 두면 기본 연결은 그대로 두고, 게임 코드만 게임 그룹 연결을 골라 쓸 수 있다.
 *
 * 접속은 처음 명령을 보낼 때 맺는다. (서버 시작 시 Redis가 꺼져 있어도 시작은 된다)
 */
public class GameRedis implements DisposableBean {

    private final LettuceConnectionFactory connectionFactory;
    private final StringRedisTemplate template;

    public GameRedis(GameRedisProperties props) {
        RedisStandaloneConfiguration config = new RedisStandaloneConfiguration(props.host(), props.port());
        config.setDatabase(props.database());
        if (props.password() != null && !props.password().isBlank()) {
            config.setPassword(RedisPassword.of(props.password()));
        }
        this.connectionFactory = new LettuceConnectionFactory(config);
        this.connectionFactory.afterPropertiesSet();
        this.template = new StringRedisTemplate(connectionFactory);
    }

    /** 키·값을 문자열로 다루는 템플릿. 게임 상태는 JSON 문자열로 저장한다. */
    public StringRedisTemplate template() {
        return template;
    }

    /** 서버가 꺼질 때 Spring이 불러 연결을 닫는다. */
    @Override
    public void destroy() {
        connectionFactory.destroy();
    }
}
