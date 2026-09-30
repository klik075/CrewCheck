package com.WhoisntCitizen_server.game.entity;

import lombok.Getter;

@Getter
public class GamePlayer {
    private final Long playerId;
    private final String nickname;
    private final Role role;
    private boolean alive = true;

    public GamePlayer(Long playerId, String nickname, Role role) {
        this.playerId = playerId;
        this.nickname = nickname;
        this.role = role;
    }

    public void kill() {
        this.alive = false;
    }

    public boolean isMafia() {
        return role.getTeam() == Team.MAFIA;
    }
}
