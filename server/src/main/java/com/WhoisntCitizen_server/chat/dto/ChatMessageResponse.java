package com.WhoisntCitizen_server.chat.dto;

import com.WhoisntCitizen_server.chat.entity.ChatMessage;
import com.WhoisntCitizen_server.chat.entity.MessageType;

import java.time.LocalDateTime;

/**
 * 메시지 전송 응답 (POST /api/v1/rooms/{roomId}/messages → 201)
 * {"messageId":100,"userId":1,"nickname":"철수","message":"2번이 마피아 같은데?","createdAt":"2026-09-28T01:10:00","type":"USER"}
 * type은 명세 추가 필드: USER(일반) / SYSTEM(시스템 메시지, userId = 0) / DEAD(사망자 채팅, 사망자에게만 보임)
 * nightChat은 명세 추가 필드: 밤에 해적이 입력한 채팅이면 true (해적에게만 보이며, 클라이언트는 주황색으로 표시)
 * sirenChat은 명세 추가 필드: 밤에 세이렌이 입력한 채팅이면 true (세이렌 팀에게만 보임)
 */
public record ChatMessageResponse(
        Long messageId,
        Long userId,
        String nickname,
        String message,
        LocalDateTime createdAt,
        MessageType type,
        boolean nightChat,
        boolean sirenChat
) {
    public static ChatMessageResponse from(ChatMessage m) {
        return new ChatMessageResponse(m.id(), m.userId(), m.nickname(), m.message(), m.createdAt(), m.type(),
                m.visibleToPiratesOnly(), m.visibleToSirenTeamOnly());
    }
}
