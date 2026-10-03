package com.WhoisntCitizen_server.lobby.event;

/**
 * 로비 방이 삭제됐을 때 발행되는 이벤트. (마지막 사람이 나감, 취소된 게임의 방 정리 등)
 * 현재는 채팅이 그 방의 메시지(Redis)를 지우는 데 사용합니다.
 */
public record RoomDeletedEvent(Long roomId) {
}
