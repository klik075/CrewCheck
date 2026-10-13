package com.WhoisntCitizen_server.chat.repository;

import com.WhoisntCitizen_server.chat.entity.ChatMessage;
import com.WhoisntCitizen_server.chat.entity.MessageType;

import java.util.List;

/** 채팅 메시지 저장소. 실제 구현은 {@link RedisChatMessageRepository}. */
public interface ChatMessageRepository {

    /**
     * 새 메시지를 저장하고, id와 createdAt이 채워진 메시지를 반환합니다.
     * gameId: 게임 중 일부에게만 보이는 메시지(사망자 채팅, 밤의 해적 채팅)면 그 게임 id, 아니면 null
     * pirateOnly: 밤에 해적이 보내 해적에게만 보이는 메시지인지
     * sirenOnly: 밤에 세이렌이 보내 세이렌 팀에게만 보이는 메시지인지
     */
    ChatMessage save(long roomId, MessageType type, Long userId, String nickname, String message,
                     String gameId, boolean pirateOnly, boolean sirenOnly);

    /** 세이렌 채팅이 아닌 메시지 저장 */
    default ChatMessage save(long roomId, MessageType type, Long userId, String nickname, String message,
                             String gameId, boolean pirateOnly) {
        return save(roomId, type, userId, nickname, message, gameId, pirateOnly, false);
    }

    /** 해적 전용이 아닌 메시지 저장 */
    default ChatMessage save(long roomId, MessageType type, Long userId, String nickname, String message, String gameId) {
        return save(roomId, type, userId, nickname, message, gameId, false);
    }

    /** 게임과 관계없는 메시지 저장 (gameId = null) */
    default ChatMessage save(long roomId, MessageType type, Long userId, String nickname, String message) {
        return save(roomId, type, userId, nickname, message, null);
    }

    /** 방의 최신 메시지 limit개를 오래된 순(id 오름차순)으로 반환합니다. */
    List<ChatMessage> findLatest(long roomId, int limit);

    /** afterId보다 큰 id의 메시지를 최대 limit개, 오래된 순으로 반환합니다. (폴링용) */
    List<ChatMessage> findAfter(long roomId, long afterId, int limit);

    /** 방의 메시지와 id 카운터(게임 시작 표시 포함)를 모두 지웁니다. (방이 삭제될 때) */
    void deleteRoom(long roomId);

    /**
     * 게임 시작 표시: 지금까지 발급된 마지막 메시지 id를 기억합니다. 이후 메시지가 "게임 중 메시지"가 됩니다.
     * 이미 표시가 있으면 덮어씁니다.
     */
    void markGameStart(long roomId);

    /**
     * 게임 시작 표시 이후의 메시지를 모두 지우고 표시도 지웁니다.
     * 표시가 없으면(서버가 표시 기능 이전에 시작한 게임 등) 아무것도 지우지 않습니다.
     * @return 지운 메시지 수
     */
    long deleteSinceGameStart(long roomId);
}
