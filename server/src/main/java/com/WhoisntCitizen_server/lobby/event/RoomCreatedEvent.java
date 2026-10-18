package com.WhoisntCitizen_server.lobby.event;

/**
 * 로비에 방이 새로 만들어졌을 때 발행되는 이벤트.
 * 방장 입장은 따로 RoomPlayerJoinedEvent로도 발행된다. (입장 알림 채팅용)
 * 현재는 실시간 모듈이 로비에 있는 사람들에게 room.created 신호를 보내는 데 사용합니다. (새로고침 버튼 RedDot)
 *
 * @param hostUserId 방을 만든 사람. 이 사람은 바로 대기실로 가므로 신호에서 뺀다.
 */
public record RoomCreatedEvent(Long roomId, Long hostUserId) {
}
