package com.WhoisntCitizen_server.lobby.event;

/**
 * 방장이 위임으로 바뀌었을 때 발행되는 이벤트.
 * 현재는 채팅이 "OOO님이 방장이 되었습니다." 시스템 메시지를 남기는 데 사용합니다.
 *
 * @param userId   새 방장 userId
 * @param nickname 새 방장 닉네임
 */
public record RoomHostChangedEvent(Long roomId, Long userId, String nickname) {
}
