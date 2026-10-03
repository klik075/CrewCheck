package com.WhoisntCitizen_server.lobby.service;

import com.WhoisntCitizen_server.common.event.RoomNoticeEvent;
import com.WhoisntCitizen_server.game.event.CancelledGameExpiredEvent;
import com.WhoisntCitizen_server.game.event.GameEndedEvent;
import com.WhoisntCitizen_server.game.event.PlayersDepartedEvent;
import lombok.RequiredArgsConstructor;
import lombok.extern.slf4j.Slf4j;
import org.springframework.context.ApplicationEventPublisher;
import org.springframework.context.event.EventListener;
import org.springframework.stereotype.Component;

/**
 * 게임 모듈이 발행한 이벤트를 받아 로비 방에 반영한다.
 * (게임 모듈은 로비를 모르고, 로비가 게임 이벤트를 구독하는 방향)
 *  - 8. 게임 종료(GameEndedEvent) → 기존 방으로 복귀(WAITING). 취소된 게임은 복귀하지 않는다
 *  - 연결 끊김(PlayersDepartedEvent) → 그 플레이어들을 방에서 뺀다
 *  - 취소된 게임 정리(CancelledGameExpiredEvent) → 방 삭제
 */
@Slf4j
@Component
@RequiredArgsConstructor
public class RoomGameListener {

    private final RoomService roomService;
    private final ApplicationEventPublisher eventPublisher; // 채팅 안내 (RoomNoticeEvent)

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
            if (event.cancelled()) {
                // 결과 조회 시간이 지나면 CancelledGameExpiredEvent로 방을 삭제한다. 그때까지 IN_GAME으로 둔다.
                log.info("[{}] 취소된 게임({}): 방 {}은 결과 조회 시간 뒤 삭제", event.gameId(), event.endReason(), roomId);
                return;
            }

            if (roomService.returnToWaiting(roomId, event.gameId())) {
                log.info("[{}] 게임 종료 → 방 {} 대기 상태로 복귀 (승리: {})", event.gameId(), roomId, event.winner());
                eventPublisher.publishEvent(RoomNoticeEvent.of(roomId, "게임이 끝나 대기실로 돌아왔습니다. 방장이 다시 게임을 시작할 수 있습니다."));
            } else {
                log.info("[{}] 게임 종료 이벤트 무시: 방 {}이 없거나 이미 복귀됨", event.gameId(), roomId);
            }
        } catch (RuntimeException e) {
            log.error("[{}] 방 복귀 처리 실패 (roomId={})", event.gameId(), event.roomId(), e);
        }
    }

    @EventListener
    public void onPlayersDeparted(PlayersDepartedEvent event) {
        try {
            Long roomId = parseRoomId(event.roomId());
            if (roomId == null) {
                return;
            }
            roomService.removeDepartedPlayers(roomId, event.gameId(), event.userIds());
            log.info("[{}] 연결이 끊긴 플레이어 {}를 방 {}에서 제외", event.gameId(), event.userIds(), roomId);
        } catch (RuntimeException e) {
            log.error("[{}] 연결이 끊긴 플레이어 방 제외 실패 (roomId={})", event.gameId(), event.roomId(), e);
        }
    }

    @EventListener
    public void onCancelledGameExpired(CancelledGameExpiredEvent event) {
        try {
            Long roomId = parseRoomId(event.roomId());
            if (roomId == null) {
                return;
            }
            if (roomService.deleteRoomOfCancelledGame(roomId, event.gameId())) {
                log.info("[{}] 취소된 게임의 방 {} 삭제", event.gameId(), roomId);
            }
        } catch (RuntimeException e) {
            // 삭제에 실패해도 방 조회 시 recoverIfOrphaned가 대기 상태로 되돌린다.
            log.error("[{}] 취소된 게임의 방 삭제 실패 (roomId={})", event.gameId(), event.roomId(), e);
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
