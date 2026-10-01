package com.WhoisntCitizen_server.lobby.service;

import com.WhoisntCitizen_server.game.event.GameEndedEvent;
import lombok.RequiredArgsConstructor;
import lombok.extern.slf4j.Slf4j;
import org.springframework.context.event.EventListener;
import org.springframework.stereotype.Component;

/**
 * 8. 게임 종료 → 기존 방으로 복귀.
 * 게임 모듈이 발행한 GameEndedEvent를 받아 방을 WAITING으로 되돌린다.
 * (게임 모듈은 로비를 모르고, 로비가 게임 이벤트를 구독하는 방향)
 */
@Slf4j
@Component
@RequiredArgsConstructor
public class RoomGameListener {

    private final RoomService roomService;

    @EventListener
    public void onGameEnded(GameEndedEvent event) {
        // 이 리스너가 실패해도 다른 리스너(전적 저장 등)는 실행되도록 예외를 여기서 처리한다.
        try {
            Long roomId = parseRoomId(event.roomId());
            if (roomId == null) {
                // Postman 테스트용 POST /api/v1/games 처럼 로비 방 없이 만든 게임
                log.debug("[{}] 로비 방과 연결되지 않은 게임 종료 (roomId={})", event.gameId(), event.roomId());
                return;
            }

            if (roomService.returnToWaiting(roomId, event.gameId())) {
                log.info("[{}] 게임 종료 → 방 {} 대기 상태로 복귀 (승리: {})", event.gameId(), roomId, event.winner());
            } else {
                log.info("[{}] 게임 종료 이벤트 무시: 방 {}이 없거나 이미 복귀됨", event.gameId(), roomId);
            }
        } catch (RuntimeException e) {
            log.error("[{}] 방 복귀 처리 실패 (roomId={})", event.gameId(), event.roomId(), e);
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
