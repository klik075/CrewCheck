package com.WhoisntCitizen_server.chat.repository;

import com.WhoisntCitizen_server.chat.entity.ChatMessage;
import com.WhoisntCitizen_server.chat.entity.MessageType;

import java.util.List;

/** 채팅 메시지 저장소. 실제 구현은 {@link RedisChatMessageRepository}. */
public interface ChatMessageRepository {

    /**
     * 새 메시지를 저장하고, id와 createdAt이 채워진 메시지를 반환합니다.
     * gameId: 게임 중 메시지(사망자 채팅 등)면 그 게임 id, 아니면 null
     */
    ChatMessage save(long roomId, MessageType type, Long userId, String nickname, String message, String gameId);

    /** 게임과 관계없는 메시지 저장 (gameId = null) */
    default ChatMessage save(long roomId, MessageType type, Long userId, String nickname, String message) {
        return save(roomId, type, userId, nickname, message, null);
    }

    /** 방의 최신 메시지 limit개를 오래된 순(id 오름차순)으로 반환합니다. */
    List<ChatMessage> findLatest(long roomId, int limit);

    /** afterId보다 큰 id의 메시지를 최대 limit개, 오래된 순으로 반환합니다. (폴링용) */
    List<ChatMessage> findAfter(long roomId, long afterId, int limit);
}
