package com.WhoisntCitizen_server.chat.repository;

import com.WhoisntCitizen_server.chat.entity.ChatMessage;
import com.WhoisntCitizen_server.chat.entity.MessageType;
import org.springframework.beans.factory.annotation.Value;
import org.springframework.data.redis.core.StringRedisTemplate;
import org.springframework.stereotype.Repository;
import tools.jackson.core.JacksonException;
import tools.jackson.databind.json.JsonMapper;

import java.time.LocalDateTime;
import java.util.ArrayList;
import java.util.Collections;
import java.util.List;
import java.util.Set;

/**
 * Redis 저장 구조 (roomId = 1 인 경우)
 *  - chat:v1:room:1:seq       (String)     메시지 id(messageId) 발급용 카운터 (INCR)
 *  - chat:v1:room:1:messages  (Sorted Set) score = 메시지 id, member = 메시지 JSON
 *
 * Sorted Set을 쓰면 "최신 N개"와 "id 이후 새 메시지"를 둘 다 빠르게 꺼낼 수 있습니다.
 * 방마다 최신 chat.max-messages-per-room 개만 남기고 오래된 메시지는 지웁니다.
 *
 * Spring Boot 4는 Jackson 3(tools.jackson)을 기본으로 쓰므로 JsonMapper를 주입받습니다.
 */
@Repository
public class RedisChatMessageRepository implements ChatMessageRepository {

    private static final String KEY_PREFIX = "chat:v1:room:";

    private final StringRedisTemplate redis;
    private final JsonMapper jsonMapper;
    private final int maxMessagesPerRoom;

    public RedisChatMessageRepository(StringRedisTemplate redis,
                                      JsonMapper jsonMapper,
                                      @Value("${chat.max-messages-per-room:1000}") int maxMessagesPerRoom) {
        this.redis = redis;
        this.jsonMapper = jsonMapper;
        this.maxMessagesPerRoom = maxMessagesPerRoom;
    }

    private static String messagesKey(long roomId) {
        return KEY_PREFIX + roomId + ":messages";
    }

    private static String seqKey(long roomId) {
        return KEY_PREFIX + roomId + ":seq";
    }

    @Override
    public ChatMessage save(long roomId, MessageType type, Long userId, String nickname, String text,
                            String gameId, boolean pirateOnly) {
        Long id = redis.opsForValue().increment(seqKey(roomId));
        ChatMessage message = new ChatMessage(id, roomId, type, userId, nickname, text, LocalDateTime.now(), gameId, pirateOnly);

        String key = messagesKey(roomId);
        redis.opsForZSet().add(key, toJson(message), id);

        if (maxMessagesPerRoom > 0) {
            // 점수(id)가 작은 것부터 지워서 최신 maxMessagesPerRoom개만 남김
            redis.opsForZSet().removeRange(key, 0, -(maxMessagesPerRoom + 1L));
        }
        return message;
    }

    @Override
    public List<ChatMessage> findLatest(long roomId, int limit) {
        Set<String> jsons = redis.opsForZSet().reverseRange(messagesKey(roomId), 0, limit - 1L);
        List<ChatMessage> result = parseAll(jsons);
        Collections.reverse(result); // 최신순 → 오래된 순
        return result;
    }

    @Override
    public List<ChatMessage> findAfter(long roomId, long afterId, int limit) {
        Set<String> jsons = redis.opsForZSet().rangeByScore(
                messagesKey(roomId), afterId + 1, Double.POSITIVE_INFINITY, 0, limit);
        return parseAll(jsons);
    }

    @Override
    public void deleteRoom(long roomId) {
        redis.delete(List.of(messagesKey(roomId), seqKey(roomId)));
    }

    private List<ChatMessage> parseAll(Set<String> jsons) {
        List<ChatMessage> result = new ArrayList<>();
        if (jsons == null) return result;
        for (String json : jsons) {
            result.add(fromJson(json));
        }
        return result;
    }

    private String toJson(ChatMessage message) {
        try {
            return jsonMapper.writeValueAsString(message);
        } catch (JacksonException e) {
            throw new IllegalStateException("메시지를 JSON으로 변환하지 못했습니다.", e);
        }
    }

    private ChatMessage fromJson(String json) {
        try {
            return jsonMapper.readValue(json, ChatMessage.class);
        } catch (JacksonException e) {
            throw new IllegalStateException("Redis에 저장된 메시지를 읽지 못했습니다: " + json, e);
        }
    }
}
