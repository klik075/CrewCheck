package com.WhoisntCitizen_server.lobby.event;

/**
 * 로비 방에 플레이어가 들어왔을 때 발행되는 이벤트 (방 생성 시 방장 입장 포함).
 * 로비는 누가 이 이벤트를 듣는지 모릅니다. 현재는 채팅이 입장 알림 메시지를 남기는 데 사용합니다.
 */
public record RoomPlayerJoinedEvent(Long roomId, Long userId, String nickname) {
}
