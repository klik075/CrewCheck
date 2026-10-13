package com.WhoisntCitizen_server.realtime;

/**
 * "이 사용자가 지금 이 방에 들어 있나"를 확인한다. 방 구독(room.subscribe)을 허락할지 정할 때 쓴다.
 * 실시간 모듈이 로비 내부(Room, LobbyRoomRepository)를 직접 알지 않도록 한 줄짜리 인터페이스로 둔다.
 */
public interface RoomMembership {

    boolean isMember(long roomId, long userId);
}
