package com.WhoisntCitizen_server.game.service;

import com.WhoisntCitizen_server.game.entity.DeathCause;
import com.WhoisntCitizen_server.game.entity.Game;
import com.WhoisntCitizen_server.game.entity.GamePlayer;
import com.WhoisntCitizen_server.game.entity.Team;
import com.WhoisntCitizen_server.game.entity.Winner;
import org.springframework.stereotype.Component;

import java.util.List;
import java.util.Optional;
import java.util.function.Predicate;

/**
 * 8. 승리 조건 검사. 누군가 죽을 때마다(밤 결과, 처형, 연결 끊김) 검사하고, 처음 충족된 조건으로 바로 끝낸다.
 * 한 번에 여러 조건이 충족되면 아래 순서가 앞선 쪽이 이긴다(달성하기 어려운 승리가 먼저).
 *
 * 머릿수: 해적 = 활동 해적(해적, 요리사, 접선한 앵무새 = GamePlayer.isActivePirate),
 *        선원 = 선원 팀(유혹되지 않은 선원 진영), 세이렌 팀 = 세이렌 + 유혹당한 사람. 모두 생존자만 센다.
 *        크라켄, 유령 선장, 유혹되지 않은 인어는 어느 쪽에도 세지 않는다.
 *
 * 1. 인어(단독): 인어가 투표로 처형되면 (유혹당한 인어 포함)
 * 2. 크라켄(단독): 크라켄이 살아 있고, 해적을 뺀 생존자(크라켄 포함)가 2명 이하
 * 3. 세이렌 팀: 세이렌이 살아 있으면 ① 해적이 전멸하고 세이렌 팀 >= 선원, 또는 ② 선원이 전멸하고 세이렌 팀 > 해적.
 *              세이렌이 죽었으면 해적과 선원이 모두 전멸하고 세이렌 팀이 남았을 때.
 * 4. 유령 선장(단독): 유령 선장이 살아 있고, 사망자 > 생존자
 * 5. 선원 팀: 해적이 전멸하고, 크라켄이 모두 죽었고, 세이렌이 살아 있으면 선원 > 세이렌 팀
 *    (접선하지 못한 앵무새는 해적 편에 합류할 수 없으므로 남아 있어도 선원 팀이 이긴다)
 * 6. 해적 팀: 해적이 남아 있고, 살아 있는 해적 진영 수(앵무새 포함) >= 살아 있는 나머지 인원 수
 *
 * 팀 승리는 그 팀 전원(사망자 포함)이, 단독 승리는 조건을 채운 그 사람만 이긴다(Victory.winnerIds).
 */
@Component
public class WinConditionChecker {

    /** 이긴 쪽과 실제로 이긴 플레이어 */
    public record Victory(Winner winner, List<Long> winnerIds) {
    }

    //승리 판정
    public Optional<Victory> check(Game game) {
        List<GamePlayer> players = game.getPlayers();
        List<GamePlayer> alive = players.stream().filter(GamePlayer::isAlive).toList();
        long dead = players.size() - alive.size();
        long activePirates = count(alive, GamePlayer::isActivePirate);
        long pirateFaction = count(alive, GamePlayer::isPirate);
        long crew = count(alive, p -> p.getTeam() == Team.CREW);
        long sirenTeam = count(alive, p -> p.getTeam() == Team.SIREN);
        boolean sirenAlive = alive.stream().anyMatch(GamePlayer::isSiren);
        boolean krakenAlive = alive.stream().anyMatch(GamePlayer::isKraken);

        // 1. 인어: 처형되면 단독 승리
        List<Long> executedMermaids = ids(players, p -> p.isMermaid() && p.getDeathCause() == DeathCause.EXECUTION);
        if (!executedMermaids.isEmpty()) {
            return victory(Winner.MERMAID, executedMermaids);
        }

        // 2. 크라켄: 해적을 뺀 생존자(크라켄 포함)가 2명 이하
        if (krakenAlive && alive.size() - activePirates <= 2) {
            return victory(Winner.KRAKEN, ids(alive, GamePlayer::isKraken));
        }

        // 3. 세이렌 팀
        boolean sirenWins = sirenAlive
                ? (activePirates == 0 && sirenTeam >= crew) || (crew == 0 && sirenTeam > activePirates)
                : activePirates == 0 && crew == 0 && sirenTeam > 0;
        if (sirenWins) {
            return victory(Winner.SIREN, ids(players, p -> p.getTeam() == Team.SIREN));
        }

        // 4. 유령 선장: 살아서 사망자 > 생존자
        if (dead > alive.size() && alive.stream().anyMatch(GamePlayer::isGhostCaptain)) {
            return victory(Winner.GHOST_CAPTAIN, ids(alive, GamePlayer::isGhostCaptain));
        }

        // 5. 선원 팀
        if (activePirates == 0 && !krakenAlive && (!sirenAlive || crew > sirenTeam)) {
            return victory(Winner.CREW, ids(players, p -> p.getTeam() == Team.CREW));
        }

        // 6. 해적 팀
        if (activePirates > 0 && pirateFaction >= alive.size() - pirateFaction) {
            return victory(Winner.PIRATE, ids(players, GamePlayer::isPirate));
        }
        return Optional.empty();
    }

    private static Optional<Victory> victory(Winner winner, List<Long> winnerIds) {
        return Optional.of(new Victory(winner, winnerIds));
    }

    private static long count(List<GamePlayer> players, Predicate<GamePlayer> filter) {
        return players.stream().filter(filter).count();
    }

    private static List<Long> ids(List<GamePlayer> players, Predicate<GamePlayer> filter) {
        return players.stream().filter(filter).map(GamePlayer::getPlayerId).toList();
    }
}
