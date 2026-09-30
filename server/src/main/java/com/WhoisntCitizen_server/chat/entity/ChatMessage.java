package com.WhoisntCitizen_server.chat.entity;

import java.time.LocalDateTime;

/**
 * 채팅 메시지. Redis에 JSON 문자열로 저장됩니다.
 * nickname은 보낸 시점의 닉네임을 함께 저장합니다.
 * 시스템 메시지(입장/퇴장 알림, 공지)는 userId가 null 입니다.
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

    public ChatMessage {
        if (type == null) type = MessageType.USER;
    }
}
