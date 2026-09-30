package com.WhoisntCitizen_server.game.entity;

import com.WhoisntCitizen_server.jobs.domain.RoleDefinition;
import lombok.Getter;

@Getter
public class GamePlayer {
    private final Long playerId;
    private final String nickname;
    private final RoleDefinition role; // DB(roles)에서 읽어 캐시한 직업 정의
    private boolean alive = true;

    public GamePlayer(Long playerId, String nickname, RoleDefinition role) {
        this.playerId = playerId;
        this.nickname = nickname;
        this.role = role;
    }

    public void kill() {
        this.alive = false;
    }

    public boolean isPirate() {
        return role.isPirate();
    }
}
