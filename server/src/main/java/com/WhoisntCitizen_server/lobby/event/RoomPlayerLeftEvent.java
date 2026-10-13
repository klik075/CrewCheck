package com.WhoisntCitizen_server.lobby.event;

/**
 * 로비 방에서 플레이어가 나갔을 때 발행되는 이벤트.
 * 현재는 채팅이 퇴장 알림 메시지를 남기는 데 사용합니다.
 */
public record RoomPlayerLeftEvent(Long roomId, Long userId, String nickname) {
}
