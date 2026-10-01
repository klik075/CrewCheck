package com.WhoisntCitizen_server.chat.dto;

import com.WhoisntCitizen_server.chat.entity.ChatMessage;
import com.WhoisntCitizen_server.chat.entity.MessageType;

/**
 * 메시지 조회 응답 항목 (GET /api/v1/rooms/{roomId}/messages → 200)
 * {"messageId":100,"userId":1,"nickname":"철수","message":"2번이 마피아 같은데?","type":"USER"}
 * type은 명세 추가 필드: USER(일반) / SYSTEM(시스템 메시지, userId = 0) / DEAD(사망자 채팅, 사망자에게만 보임)
 * nightChat은 명세 추가 필드: 밤에 해적이 입력한 채팅이면 true (해적에게만 내려가며, 클라이언트는 주황색으로 표시)
 */
public record ChatMessageSummary(
        Long messageId,
        Long userId,
        String nickname,
        String message,
        MessageType type,
        boolean nightChat
) {
    public static ChatMessageSummary from(ChatMessage m) {
        return new ChatMessageSummary(m.id(), m.userId(), m.nickname(), m.message(), m.type(), m.visibleToPiratesOnly());
    }
}
