package com.WhoisntCitizen_server.game.service;

import com.WhoisntCitizen_server.game.entity.Game;
import com.WhoisntCitizen_server.game.entity.GamePlayer;
import com.WhoisntCitizen_server.game.entity.Team;
import org.springframework.stereotype.Component;

import java.util.Optional;

/**
 * 8. 승리 조건 검사.
 * - 시민팀: 살아 있는 마피아가 0명
 * - 마피아팀: 살아 있는 마피아 수 >= 살아 있는 시민팀 수
 */
@Component
public class WinConditionChecker {
    //승리 판별
    public Optional<Team> check(Game game) {
        long aliveMafia = game.getPlayers().stream().filter(p -> p.isAlive() && p.isMafia()).count();
        long aliveCitizen = game.getPlayers().stream().filter(GamePlayer::isAlive).count() - aliveMafia;

        if (aliveMafia == 0) {
            return Optional.of(Team.CITIZEN);
        }
        if (aliveMafia >= aliveCitizen) {
            return Optional.of(Team.MAFIA);
        }
        return Optional.empty();
    }
}
