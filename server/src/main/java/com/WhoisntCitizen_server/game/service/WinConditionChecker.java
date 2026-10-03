package com.WhoisntCitizen_server.game.service;

import com.WhoisntCitizen_server.game.entity.Game;
import com.WhoisntCitizen_server.game.entity.GamePlayer;
import com.WhoisntCitizen_server.jobs.domain.Faction;
import org.springframework.stereotype.Component;

import java.util.Optional;

/**
 * 8. 승리 조건 검사
 * - 선원팀(CREW): 해적 편으로 활동하는 생존자(해적 + 해적과 접선한 앵무새)가 0명
 *   접선하지 못한 앵무새는 해적이 모두 죽으면 해적 편에 합류할 수 없으므로 남아 있어도 선원팀이 이긴다.
 *   접선한 앵무새가 살아 있으면 해적이 모두 죽어도 게임이 이어진다.
 * - 해적팀(PIRATE): 살아 있는 해적 진영 수(앵무새 포함) >= 살아 있는 나머지 인원 수
 */
@Component
public class WinConditionChecker {
    //승리 판정
    public Optional<Faction> check(Game game) {
        long activePirates = game.getPlayers().stream()
                .filter(p -> p.isAlive() && (p.isRaider() || (p.isParrot() && p.isContacted())))
                .count();
        long aliveMafia = game.getPlayers().stream().filter(p -> p.isAlive() && p.isPirate()).count();
        long aliveCitizen = game.getPlayers().stream().filter(GamePlayer::isAlive).count() - aliveMafia;

        if (activePirates == 0) {
            return Optional.of(Faction.CREW);
        }
        if (aliveMafia >= aliveCitizen) {
            return Optional.of(Faction.PIRATE);
        }
        return Optional.empty();
    }
}
