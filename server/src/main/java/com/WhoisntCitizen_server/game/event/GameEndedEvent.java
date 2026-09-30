package com.WhoisntCitizen_server.game.event;

import com.WhoisntCitizen_server.game.entity.Game;
import com.WhoisntCitizen_server.jobs.domain.Faction;

import java.util.List;

/**
 * 게임이 끝났을 때 발행하는 이벤트. 게임 모듈은 로비/회원 코드를 모르고 이 이벤트만 발행한다.
 * - 로비: roomId로 방을 찾아 WAITING으로 되돌린다 (기존 방으로 복귀)
 * - 회원: outcomes로 User 전적(playCount, winCount)을 저장한다
 * 받는 쪽은 @EventListener 로 구독한다.
 */
public record GameEndedEvent(
        String gameId,
        String roomId,
        Faction winner,
        int lastDay,
        boolean recordStats,          // true면 회원 전적에 반영 (방에서 시작한 실제 게임)
        List<PlayerOutcome> outcomes
) {
    /** userId = User(프로필)의 id = 게임의 playerId */
    public record PlayerOutcome(Long userId, String roleCode, Faction faction, boolean alive, boolean win) {
    }

    public static GameEndedEvent from(Game game) {
        Faction winner = game.getWinner();
        List<PlayerOutcome> outcomes = game.getPlayers().stream()
                .map(p -> new PlayerOutcome(
                        p.getPlayerId(),
                        p.getRole().code(),
                        p.getRole().faction(),
                        p.isAlive(),
                        p.getRole().faction() == winner))
                .toList();
        return new GameEndedEvent(game.getGameId(), game.getRoomId(), winner, game.getDay(), game.isRecordStats(), outcomes);
    }
}
