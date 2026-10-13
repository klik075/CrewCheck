package com.WhoisntCitizen_server.game.service;

import com.WhoisntCitizen_server.game.entity.Game;
import com.WhoisntCitizen_server.game.entity.GamePlayer;
import com.WhoisntCitizen_server.game.entity.Winner;
import com.WhoisntCitizen_server.jobs.domain.ActionCode;
import com.WhoisntCitizen_server.jobs.domain.Faction;
import com.WhoisntCitizen_server.jobs.domain.RoleDefinition;
import org.junit.jupiter.api.Test;

import java.time.Instant;
import java.util.List;
import java.util.Optional;

import static org.assertj.core.api.Assertions.assertThat;

/** 8. 승리 조건. 앵무새는 해적과 접선했는지에 따라 해적이 모두 죽은 뒤의 결과가 달라진다. */
class WinConditionCheckerTest {

    private static final RoleDefinition RAIDER =
            new RoleDefinition("PIRATE_RAIDER", "해적", Faction.PIRATE, ActionCode.SELECT_ATTACK_TARGET);
    private static final RoleDefinition PARROT =
            new RoleDefinition("PIRATE_PARROT", "앵무새", Faction.PIRATE, ActionCode.WATCH_ACTION);
    private static final RoleDefinition SAILOR =
            new RoleDefinition("CREW_SAILOR", "선원", Faction.CREW, null);

    private final WinConditionChecker checker = new WinConditionChecker();

    private Optional<Winner> winnerOf(Game game) {
        return checker.check(game).map(WinConditionChecker.Victory::winner);
    }

    // 1: 해적, 2: 앵무새, 3~6: 선원
    private Game newGame() {
        return new Game("room-1", List.of(
                new GamePlayer(1L, "해적", RAIDER),
                new GamePlayer(2L, "앵무새", PARROT),
                new GamePlayer(3L, "선원3", SAILOR),
                new GamePlayer(4L, "선원4", SAILOR),
                new GamePlayer(5L, "선원5", SAILOR),
                new GamePlayer(6L, "선원6", SAILOR)));
    }

    @Test
    void 해적이_살아_있고_수가_적으면_게임이_이어진다() {
        assertThat(checker.check(newGame())).isEmpty();
    }

    @Test
    void 접선하지_못한_앵무새만_남으면_선원팀이_이긴다() {
        Game game = newGame();
        game.getPlayer(1L).kill();

        assertThat(winnerOf(game)).contains(Winner.CREW);
    }

    @Test
    void 접선한_앵무새가_살아_있으면_해적이_모두_죽어도_게임이_이어진다() {
        Game game = newGame();
        game.getPlayer(2L).markContacted(Instant.parse("2026-10-01T12:00:00Z"));
        game.getPlayer(1L).kill();

        assertThat(checker.check(game)).isEmpty();
    }

    @Test
    void 접선한_앵무새가_나머지_인원_이상이면_해적팀이_이긴다() {
        Game game = newGame();
        game.getPlayer(2L).markContacted(Instant.parse("2026-10-01T12:00:00Z"));
        game.getPlayer(1L).kill();
        game.getPlayer(3L).kill();
        game.getPlayer(4L).kill();
        game.getPlayer(5L).kill();

        assertThat(winnerOf(game)).contains(Winner.PIRATE);
    }

    @Test
    void 접선한_앵무새도_죽으면_선원팀이_이긴다() {
        Game game = newGame();
        game.getPlayer(2L).markContacted(Instant.parse("2026-10-01T12:00:00Z"));
        game.getPlayer(1L).kill();
        game.getPlayer(2L).kill();

        assertThat(winnerOf(game)).contains(Winner.CREW);
    }

    @Test
    void 해적_진영이_나머지_인원_이상이면_해적팀이_이긴다() {
        Game game = newGame();
        game.getPlayer(3L).kill();
        game.getPlayer(4L).kill();

        assertThat(winnerOf(game)).contains(Winner.PIRATE);
    }
}
