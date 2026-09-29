package com.sparta.unityaitestproject_server.game.dto;

import com.sparta.unityaitestproject_server.game.entity.Role;
import com.sparta.unityaitestproject_server.game.entity.Team;

import java.util.List;

/** 8~9. 승리 결과. 게임이 끝나면 전원의 역할을 공개한다. */
public record GameResultResponse(boolean ended, Team winner, int lastDay, List<PlayerResult> players) {
    public record PlayerResult(Long playerId, String nickname, Role role, boolean alive) {
    }
}
