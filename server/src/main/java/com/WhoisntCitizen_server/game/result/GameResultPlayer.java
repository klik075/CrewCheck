package com.WhoisntCitizen_server.game.result;

import com.WhoisntCitizen_server.game.event.GameEndedEvent;
import com.WhoisntCitizen_server.jobs.domain.Faction;
import jakarta.persistence.Column;
import jakarta.persistence.Embeddable;
import jakarta.persistence.EnumType;
import jakarta.persistence.Enumerated;

/** 게임 결과의 참가자 한 명 (game_result_players). userId = User(프로필)의 id = 게임의 playerId */
@Embeddable
public class GameResultPlayer {

    @Column(name = "user_id", nullable = false)
    private Long userId;

    @Column(name = "role_code", nullable = false)
    private String roleCode;

    @Enumerated(EnumType.STRING)
    private Faction faction;

    @Column(nullable = false)
    private boolean alive;

    @Column(nullable = false)
    private boolean win;

    protected GameResultPlayer() {}

    GameResultPlayer(GameEndedEvent.PlayerOutcome outcome) {
        this.userId = outcome.userId();
        this.roleCode = outcome.roleCode();
        this.faction = outcome.faction();
        this.alive = outcome.alive();
        this.win = outcome.win();
    }

    public Long getUserId() { return userId; }
    public String getRoleCode() { return roleCode; }
    public Faction getFaction() { return faction; }
    public boolean isAlive() { return alive; }
    public boolean isWin() { return win; }
}
