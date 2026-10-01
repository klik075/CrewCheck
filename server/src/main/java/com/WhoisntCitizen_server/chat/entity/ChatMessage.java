package com.WhoisntCitizen_server.chat.entity;

import java.time.LocalDateTime;

/**
 * 채팅 메시지. Redis에 JSON 문자열로 저장됩니다.
 * nickname은 보낸 시점의 닉네임을 함께 저장합니다.
 * 시스템 메시지(입장/퇴장 알림, 공지)는 userId가 0(SYSTEM_USER_ID) 입니다.
 */
public record ChatMessage(
        Long id,
        Long roomId,
        MessageType type,
        Long userId,
        String nickname,
        String message,
        LocalDateTime createdAt
) {
    public static final int MAX_MESSAGE_LENGTH = 1000;

    /** 시스템 메시지의 nickname 값 */
    public static final String SYSTEM_NICKNAME = "SYSTEM";

    /** 시스템 메시지의 userId 값 (실제 유저 id는 1부터 시작하므로 겹치지 않음) */
    public static final long SYSTEM_USER_ID = 0L;

    public ChatMessage {
        if (type == null) type = MessageType.USER;
        // 시스템 메시지는 userId를 0으로 고정 (이전에 null로 저장된 Redis 데이터도 읽을 때 0으로 맞춤)
        if (type == MessageType.SYSTEM) userId = SYSTEM_USER_ID;
    }
}
