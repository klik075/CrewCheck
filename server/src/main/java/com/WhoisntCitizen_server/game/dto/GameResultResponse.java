package com.WhoisntCitizen_server.game.dto;

import com.WhoisntCitizen_server.game.entity.GamePlayer;
import com.WhoisntCitizen_server.jobs.domain.Faction;

import java.util.List;

/** 8~9. 승리 결과. 게임이 끝나면 전원의 역할을 공개한다. */
public record GameResultResponse(boolean ended, Faction winner, int lastDay, List<PlayerResult> players) {
    public record PlayerResult(Long playerId, String nickname, String role, String roleName, boolean alive) {
        public static PlayerResult from(GamePlayer p) {
            return new PlayerResult(p.getPlayerId(), p.getNickname(), p.getRole().code(), p.getRole().name(), p.isAlive());
        }
    }
}
