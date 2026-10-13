package com.WhoisntCitizen_server.game.event;

import java.util.List;

/**
 * 연결이 끊겨 게임에서 내보낸 플레이어가 생겼을 때 발행하는 이벤트.
 * 게임은 이미 사망 처리를 끝낸 상태이며, 로비가 받아 해당 방에서 이 플레이어들을 뺀다.
 * userIds = User(프로필)의 id = 게임의 playerId
 */
public record PlayersDepartedEvent(String gameId, String roomId, List<Long> userIds) {
}
