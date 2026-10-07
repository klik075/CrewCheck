package com.WhoisntCitizen_server.game.dto;

import com.WhoisntCitizen_server.game.entity.Game;
import com.WhoisntCitizen_server.game.entity.GameEndReason;
import com.WhoisntCitizen_server.game.entity.GamePhase;
import com.WhoisntCitizen_server.game.entity.Winner;

import java.time.Instant;
import java.util.List;

/**
 * 3/5/6/9. 현재 게임 상태 (모든 플레이어에게 공개되는 정보만).
 * 클라이언트는 이 API를 주기적으로 조회해 phase가 바뀌면 화면을 전환한다.
 * serverTime = 응답을 만든 서버 시각. 남은 시간은 phaseEndsAt - serverTime으로 계산한다(기기 시계가 서버와 달라도 맞게).
 * endReason = ENDED일 때 끝난 이유(WIN / CANCELLED_*). 취소된 게임은 winner가 null이다. 진행 중이면 null.
 * winner = 이긴 쪽(CREW / PIRATE / SIREN / KRAKEN / GHOST_CAPTAIN / MERMAID). 실제로 이긴 사람은 결과 조회의 winnerIds.
 */
public record GameStateResponse(
        String gameId,
        GamePhase phase,
        int day,
        Instant phaseEndsAt,
        Instant serverTime,
        long phaseVersion,
        List<PlayerView> players,
        Winner winner,
        GameEndReason endReason
) {
    public record PlayerView(Long playerId, String nickname, boolean alive) {
    }

    public static GameStateResponse from(Game game, Instant serverTime) {
        List<PlayerView> views = game.getPlayers().stream()
                .map(p -> new PlayerView(p.getPlayerId(), p.getNickname(), p.isAlive()))
                .toList();
        return new GameStateResponse(game.getGameId(), game.getPhase(), game.getDay(),
                game.getPhaseEndsAt(), serverTime, game.getPhaseVersion(), views, game.getWinner(), game.getEndReason());
    }
}
