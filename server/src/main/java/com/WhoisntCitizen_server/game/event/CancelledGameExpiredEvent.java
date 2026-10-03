package com.WhoisntCitizen_server.game.event;

/**
 * 취소된 게임의 결과 조회 시간(endedRetentionSeconds)이 끝났을 때, 게임을 메모리에서 지우기 직전에 발행하는 이벤트.
 * 로비가 받아 그 게임의 방을 삭제한다. (취소 직후가 아니라 이때 지워서 클라이언트가 취소 안내와 결과를 볼 수 있게 한다)
 * 게임이 아직 메모리에 있는 동안 방을 지워야, 그사이 방 조회가 방을 대기 상태로 되돌리지 않는다. (GameService.keepsRoomInGame)
 */
public record CancelledGameExpiredEvent(String gameId, String roomId) {
}
