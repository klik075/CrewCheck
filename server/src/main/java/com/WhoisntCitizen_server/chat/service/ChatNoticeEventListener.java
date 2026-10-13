package com.WhoisntCitizen_server.chat.service;

import com.WhoisntCitizen_server.common.event.PirateNoticeEvent;
import com.WhoisntCitizen_server.common.event.RoomNoticeEvent;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;
import org.springframework.context.event.EventListener;
import org.springframework.stereotype.Component;

/**
 * 게임/로비/전적 모듈이 발행한 안내(RoomNoticeEvent)를 방 채팅에 시스템 메시지로 남깁니다.
 * 예) "1일차 밤이 되었습니다. 30초 동안 능력을 사용해 주세요.", "게임이 끝나 대기실로 돌아왔습니다. ..."
 * 해적 전용 안내(PirateNoticeEvent)는 같은 게임의 해적에게만 보이는 시스템 메시지로 남깁니다.
 * 채팅 저장(Redis)에 실패해도 게임·로비 진행은 막지 않도록 경고 로그만 남깁니다.
 */
@Component
public class ChatNoticeEventListener {

    private static final Logger log = LoggerFactory.getLogger(ChatNoticeEventListener.class);

    private final ChatMessageService chatMessageService;

    public ChatNoticeEventListener(ChatMessageService chatMessageService) {
        this.chatMessageService = chatMessageService;
    }

    @EventListener
    public void onNotice(RoomNoticeEvent event) {
        Long roomId = parseRoomId(event.roomId());
        if (roomId == null || event.message() == null || event.message().isBlank()) {
            return; // 로비 방과 연결되지 않은 게임(개발용) 등
        }
        try {
            chatMessageService.saveSystem(roomId, event.message());
            log.info("[Chat][시스템] room {}: {}", roomId, event.message());
        } catch (RuntimeException e) {
            log.warn("[Chat] 시스템 안내 저장 실패 (roomId={}): {}", roomId, e.getMessage());
        }
    }

    /** 해적에게만 보이는 안내 (해적의 공격 대상 선택, 앵무새 접선 등) */
    @EventListener
    public void onPirateNotice(PirateNoticeEvent event) {
        Long roomId = parseRoomId(event.roomId());
        if (roomId == null || event.gameId() == null || event.message() == null || event.message().isBlank()) {
            return;
        }
        try {
            chatMessageService.saveSystemForPirates(roomId, event.gameId(), event.message());
            log.info("[Chat][시스템][해적 전용] room {}: {}", roomId, event.message());
        } catch (RuntimeException e) {
            log.warn("[Chat] 해적 안내 저장 실패 (roomId={}): {}", roomId, e.getMessage());
        }
    }

    private static Long parseRoomId(String roomId) {
        try {
            return roomId == null ? null : Long.valueOf(roomId);
        } catch (NumberFormatException e) {
            return null;
        }
    }
}
