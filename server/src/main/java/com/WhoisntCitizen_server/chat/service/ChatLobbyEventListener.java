package com.WhoisntCitizen_server.chat.service;

import com.WhoisntCitizen_server.lobby.event.RoomDeletedEvent;
import com.WhoisntCitizen_server.lobby.event.RoomHostChangedEvent;
import com.WhoisntCitizen_server.lobby.event.RoomPlayerKickedEvent;
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
 *  - 추방: "OOO님이 추방되었습니다."
 *  - 방장 위임: "OOO님이 방장이 되었습니다."
 * 방이 삭제되면 그 방의 채팅(Redis)을 지웁니다. (방 id는 다시 쓰이지 않으므로 지우지 않으면 계속 쌓인다)
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

    @EventListener
    public void onKicked(RoomPlayerKickedEvent event) {
        post(event.roomId(), displayName(event.nickname(), event.userId()) + "님이 추방되었습니다.");
    }

    @EventListener
    public void onHostChanged(RoomHostChangedEvent event) {
        post(event.roomId(), displayName(event.nickname(), event.userId()) + "님이 방장이 되었습니다.");
    }

    @EventListener
    public void onRoomDeleted(RoomDeletedEvent event) {
        try {
            chatMessageService.deleteRoomMessages(event.roomId());
            log.info("[Chat] 삭제된 방의 채팅 정리 (roomId={})", event.roomId());
        } catch (RuntimeException e) {
            log.warn("[Chat] 삭제된 방의 채팅 정리 실패 (roomId={}): {}", event.roomId(), e.getMessage());
        }
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
