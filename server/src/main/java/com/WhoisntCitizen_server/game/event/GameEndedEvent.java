package com.WhoisntCitizen_server.game.event;

import com.WhoisntCitizen_server.game.entity.Game;
import com.WhoisntCitizen_server.game.entity.GameEndReason;
import com.WhoisntCitizen_server.game.entity.Winner;
import com.WhoisntCitizen_server.jobs.domain.Faction;

import java.util.List;

/**
 * 게임이 끝났을 때 발행하는 이벤트. 게임 모듈은 로비/회원 코드를 모르고 이 이벤트만 발행한다.
 * - 로비: roomId로 방을 찾아 WAITING으로 되돌린다 (기존 방으로 복귀). 취소된 게임은 되돌리지 않고 나중에 방을 삭제한다
 * - 회원: outcomes로 User 전적(playCount, winCount)을 저장한다. 취소된 게임은 저장하지 않는다
 * 받는 쪽은 @EventListener 로 구독한다.
 */
public record GameEndedEvent(
        String gameId,
        String roomId,
        Winner winner,                // 취소된 게임이면 null
        GameEndReason endReason,
        int lastDay,
        boolean recordStats,          // true면 회원 전적에 반영 (방에서 시작한 실제 게임)
        List<PlayerOutcome> outcomes
) {
    /** userId = User(프로필)의 id = 게임의 playerId. win은 Game.winnerIds 기준(팀 승리면 사망자 포함, 단독 승리면 그 사람만) */
    public record PlayerOutcome(Long userId, String roleCode, Faction faction, boolean alive, boolean win) {
    }

    public static GameEndedEvent from(Game game) {
        List<Long> winnerIds = game.getWinnerIds();
        List<PlayerOutcome> outcomes = game.getPlayers().stream()
                .map(p -> new PlayerOutcome(
                        p.getPlayerId(),
                        p.getRole().code(),
                        p.getRole().faction(),
                        p.isAlive(),
                        winnerIds.contains(p.getPlayerId())))
                .toList();
        return new GameEndedEvent(game.getGameId(), game.getRoomId(), game.getWinner(), game.getEndReason(), game.getDay(),
                game.isRecordStats(), outcomes);
    }

    /** 승리 팀 없이 취소된 게임인지 */
    public boolean cancelled() {
        return endReason != null && endReason.isCancelled();
    }
}
