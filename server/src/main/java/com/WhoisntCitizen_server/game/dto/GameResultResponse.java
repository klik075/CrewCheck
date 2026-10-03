package com.WhoisntCitizen_server.game.dto;

import com.WhoisntCitizen_server.game.entity.GameEndReason;
import com.WhoisntCitizen_server.game.entity.GamePlayer;
import com.WhoisntCitizen_server.jobs.domain.Faction;

import java.util.List;

/**
 * 8~9. 승리 결과. 게임이 끝나면 전원의 실제 직업을 공개한다(원숭이도 위장 직업이 아니라 원숭이로).
 * endReason = 끝난 이유(WIN / CANCELLED_*). 취소된 게임은 winner가 null이다. 종료 전이면 null.
 */
public record GameResultResponse(boolean ended, Faction winner, GameEndReason endReason, int lastDay,
                                 List<PlayerResult> players) {
    public record PlayerResult(Long playerId, String nickname, String role, String roleName, boolean alive) {
        public static PlayerResult from(GamePlayer p) {
            return new PlayerResult(p.getPlayerId(), p.getNickname(), p.getRole().code(), p.getRole().name(), p.isAlive());
        }
    }
}
