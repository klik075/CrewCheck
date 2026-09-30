package com.WhoisntCitizen_server.game.entity;

public enum Role {
    MAFIA(Team.MAFIA, true),
    POLICE(Team.CITIZEN, true),
    DOCTOR(Team.CITIZEN, true),
    CITIZEN(Team.CITIZEN, false);

    private final Team team;
    private final boolean hasNightAction;

    Role(Team team, boolean hasNightAction) {
        this.team = team;
        this.hasNightAction = hasNightAction;
    }

    public Team getTeam() {
        return team;
    }

    public boolean hasNightAction() {
        return hasNightAction;
    }
}
