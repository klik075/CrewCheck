package com.WhoisntCitizen_server.lobby.event;

/**
 * 방장이 플레이어를 추방했을 때 발행되는 이벤트.
 * 현재는 채팅이 "OOO님이 추방되었습니다." 시스템 메시지를 남기는 데 사용합니다.
 */
public record RoomPlayerKickedEvent(Long roomId, Long userId, String nickname) {
}
