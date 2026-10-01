package com.WhoisntCitizen_server.chat.service;

import com.WhoisntCitizen_server.lobby.event.RoomPlayerJoinedEvent;
import com.WhoisntCitizen_server.lobby.event.RoomPlayerLeftEvent;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;
import org.springframework.context.event.EventListener;
import org.springframework.stereotype.Component;

/**
 * 로비 방 입장/퇴장 이벤트를 받아 채팅에 시스템 메시지를 남깁니다.
 *  - 입장: "OOO님이 입장했습니다."
 *  - 퇴장: "OOO님이 퇴장했습니다."
 * 채팅 저장(Redis)에 실패해도 로비 동작(입장/퇴장)은 막지 않도록 경고 로그만 남깁니다.
 */
@Component
public class ChatLobbyEventListener {

    private static final Logger log = LoggerFactory.getLogger(ChatLobbyEventListener.class);

    private final ChatMessageService chatMessageService;

    public ChatLobbyEventListener(ChatMessageService chatMessageService) {
        this.chatMessageService = chatMessageService;
    }

    @EventListener
    public void onJoined(RoomPlayerJoinedEvent event) {
        post(event.roomId(), displayName(event.nickname(), event.userId()) + "님이 입장했습니다.");
    }

    @EventListener
    public void onLeft(RoomPlayerLeftEvent event) {
        post(event.roomId(), displayName(event.nickname(), event.userId()) + "님이 퇴장했습니다.");
    }

    private void post(Long roomId, String message) {
        try {
            chatMessageService.saveSystem(roomId, message);
        } catch (RuntimeException e) {
            log.warn("[Chat] 시스템 메시지 저장 실패 (roomId={}): {}", roomId, e.getMessage());
        }
    }

    private static String displayName(String nickname, Long userId) {
        return (nickname == null || nickname.isBlank()) ? String.valueOf(userId) : nickname;
    }
}
