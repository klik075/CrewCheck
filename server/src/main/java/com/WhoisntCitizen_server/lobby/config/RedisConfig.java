package com.WhoisntCitizen_server.lobby.config;

import com.WhoisntCitizen_server.lobby.domain.room.Room;
import org.springframework.context.annotation.Bean;
import org.springframework.context.annotation.Configuration;
import org.springframework.data.redis.connection.RedisConnectionFactory;
import org.springframework.data.redis.core.RedisTemplate;
import org.springframework.data.redis.serializer.JacksonJsonRedisSerializer;
import org.springframework.data.redis.serializer.StringRedisSerializer;

@Configuration
public class RedisConfig {

    @Bean
    public RedisTemplate<String, Room> roomRedisTemplate(RedisConnectionFactory connectionFactory) {

        RedisTemplate<String, Room> template = new RedisTemplate<>();
        template.setConnectionFactory(connectionFactory);

        // Key: "room:1" 같은 문자열
        template.setKeySerializer(new StringRedisSerializer());

        // Value: Room 객체를 JSON으로 변환
        template.setValueSerializer(new JacksonJsonRedisSerializer<>(Room.class));

        return template;
    }
}
