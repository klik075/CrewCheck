package com.WhoisntCitizen_server.game.dto;

import com.WhoisntCitizen_server.game.entity.GameEndReason;
import com.WhoisntCitizen_server.game.entity.GamePlayer;
import com.WhoisntCitizen_server.game.entity.Team;
import com.WhoisntCitizen_server.game.entity.Winner;

import java.util.List;

/**
 * 8~9. 승리 결과. 게임이 끝나면 전원의 실제 직업을 공개한다(원숭이도 위장 직업이 아니라 원숭이로).
 * endReason = 끝난 이유(WIN / CANCELLED_*). 취소된 게임은 winner가 null이다. 종료 전이면 null.
 * winner = 이긴 쪽, winnerIds = 실제로 이긴 플레이어(팀 승리면 그 팀 전원(사망자 포함), 단독 승리면 그 사람만).
 * players[].team = 끝났을 때의 팀(세이렌에게 유혹당했으면 SIREN), players[].win = 이긴 사람인지.
 */
public record GameResultResponse(boolean ended, Winner winner, GameEndReason endReason, int lastDay,
                                 List<PlayerResult> players, List<Long> winnerIds) {

    public GameResultResponse(boolean ended, Winner winner, GameEndReason endReason, int lastDay,
                              List<PlayerResult> players) {
        this(ended, winner, endReason, lastDay, players, List.of());
    }

    public record PlayerResult(Long playerId, String nickname, String role, String roleName, boolean alive,
                               Team team, boolean win) {
        /** 승리 여부를 모를 때(종료 전 등) */
        public static PlayerResult from(GamePlayer p) {
            return from(p, List.of());
        }

        public static PlayerResult from(GamePlayer p, List<Long> winnerIds) {
            return new PlayerResult(p.getPlayerId(), p.getNickname(), p.getRole().code(), p.getRole().name(), p.isAlive(),
                    p.getTeam(), winnerIds.contains(p.getPlayerId()));
        }
    }
}
