package com.WhoisntCitizen_server.lobby.event;

/**
 * 방이 저장되거나(입장·퇴장·준비·방장 변경·강퇴·게임 시작·복귀·복구 등) 삭제됐다.
 * LobbyRoomRepository가 save/delete 직후 발행한다. 모든 방 변경은 저장소를 거치므로 빠지는 경우가 없다.
 *
 * 받는 쪽: 실시간 모듈(RealtimeRoomListener)이 그 방 구독자에게 room.changed를 보낸다.
 * 입장·퇴장 같은 "무슨 일이 있었나"가 필요하면 기존 이벤트(RoomPlayerJoinedEvent 등)를 쓴다.
 * 이 이벤트는 "다시 가져가라"는 신호용이라 roomId만 담는다.
 *
 * @param deleted 방이 삭제됐으면 true
 */
public record RoomStateChangedEvent(Long roomId, boolean deleted) {
}
