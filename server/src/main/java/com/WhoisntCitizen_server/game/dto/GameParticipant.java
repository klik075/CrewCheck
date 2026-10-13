package com.WhoisntCitizen_server.game.dto;

/**
 * 게임에 참가하는 플레이어 한 명 (서버 내부용).
 * 로비의 방 시작 처리가 Room의 참가자 목록을 이 형태로 바꿔 GameService.startGame에 넘긴다.
 * userId = User(프로필)의 id이며, 게임 안에서는 그대로 playerId로 사용한다.
 */
public record GameParticipant(Long userId, String nickname) {
}
