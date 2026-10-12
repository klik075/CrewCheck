package com.WhoisntCitizen_server.game.event;

import com.WhoisntCitizen_server.game.entity.Game;

/**
 * 게임 상태가 저장됐다. (페이즈 전환, 투표, 밤 행동, 연결 끊김, 종료 등 저장되는 모든 변경)
 * 게임 잠금이 풀린 뒤에 발행된다. 같은 잠금 안에서 여러 번 저장해도 게임마다 한 번만, 마지막 상태로 발행된다.
 * (ChangeEventGameRepository)
 *
 * 발행하는 쪽(게임)은 받는 쪽을 모른다. 지금은 실시간 모듈(RealtimeGameListener)이 받아 그 방에 game.changed를 보낸다.
 * roomId는 로비 방 id 문자열이다. 숫자가 아니면(개발용 POST /api/v1/games 게임 등) 받는 쪽이 무시한다.
 */
public record GameStateChangedEvent(String gameId, String roomId, long phaseVersion) {

    public static GameStateChangedEvent of(Game game) {
        return new GameStateChangedEvent(game.getGameId(), game.getRoomId(), game.getPhaseVersion());
    }
}
