package com.WhoisntCitizen_server.game.service;

import com.WhoisntCitizen_server.game.entity.DeathCause;
import com.WhoisntCitizen_server.game.entity.Game;
import com.WhoisntCitizen_server.game.entity.GamePlayer;
import com.WhoisntCitizen_server.game.entity.Winner;
import com.WhoisntCitizen_server.game.event.GameEndedEvent;
import com.WhoisntCitizen_server.jobs.domain.ActionCode;
import com.WhoisntCitizen_server.jobs.domain.Faction;
import com.WhoisntCitizen_server.jobs.domain.RoleDefinition;
import org.junit.jupiter.api.Test;

import java.time.Instant;
import java.util.ArrayList;
import java.util.List;
import java.util.Optional;

import static org.assertj.core.api.Assertions.assertThat;

/**
 * 8. 제3 세력이 있는 승리 조건과 우선순위 (인어 → 크라켄 → 세이렌 팀 → 유령 선장 → 선원 → 해적).
 * 팀 승리는 그 팀 전원(사망자 포함), 단독 승리는 그 사람만 이긴다.
 */
class NeutralWinConditionTest {

    private static final RoleDefinition RAIDER =
            new RoleDefinition("PIRATE_RAIDER", "해적", Faction.PIRATE, ActionCode.SELECT_ATTACK_TARGET);
    private static final RoleDefinition PARROT =
            new RoleDefinition("PIRATE_PARROT", "앵무새", Faction.PIRATE, ActionCode.WATCH_ACTION);
    private static final RoleDefinition SIREN =
            new RoleDefinition("NEUTRAL_SIREN", "세이렌", Faction.NEUTRAL, ActionCode.SEDUCE);
    private static final RoleDefinition KRAKEN =
            new RoleDefinition("NEUTRAL_KRAKEN", "크라켄", Faction.NEUTRAL, ActionCode.KRAKEN_MARK);
    private static final RoleDefinition GHOST =
            new RoleDefinition("NEUTRAL_GHOST_CAPTAIN", "유령 선장", Faction.NEUTRAL, null);
    private static final RoleDefinition MERMAID =
            new RoleDefinition("NEUTRAL_MERMAID", "인어", Faction.NEUTRAL, null);
    private static final RoleDefinition SAILOR =
            new RoleDefinition("CREW_SAILOR", "선원", Faction.CREW, null);

    private final WinConditionChecker checker = new WinConditionChecker();

    /** roles 순서대로 playerId 1, 2, 3... */
    private static Game game(RoleDefinition... roles) {
        List<GamePlayer> players = new ArrayList<>();
        for (int i = 0; i < roles.length; i++) {
            players.add(new GamePlayer((long) (i + 1), "p" + (i + 1), roles[i]));
        }
        return new Game("room-1", players);
    }

    private static void kill(Game game, long... ids) {
        for (long id : ids) {
            game.getPlayer(id).kill(DeathCause.ATTACK);
        }
    }

    private static void seduce(Game game, long... ids) {
        for (long id : ids) {
            game.getPlayer(id).joinSirenTeam(Instant.now());
        }
    }

    private Optional<Winner> winnerOf(Game game) {
        return checker.check(game).map(WinConditionChecker.Victory::winner);
    }

    private List<Long> winnerIdsOf(Game game) {
        return checker.check(game).orElseThrow().winnerIds();
    }

    // ---------- 인어 ----------

    @Test
    void 인어가_처형되면_인어_혼자_이긴다() {
        Game game = game(RAIDER, MERMAID, SAILOR, SAILOR, SAILOR, SAILOR);
        game.getPlayer(2L).kill(DeathCause.EXECUTION);

        assertThat(winnerOf(game)).contains(Winner.MERMAID);
        assertThat(winnerIdsOf(game)).containsExactly(2L);
    }

    @Test
    void 밤에_죽은_인어는_이기지_못한다() {
        Game game = game(RAIDER, MERMAID, SAILOR, SAILOR, SAILOR, SAILOR);
        kill(game, 2L);

        assertThat(winnerOf(game)).isEmpty();
    }

    @Test
    void 유혹당한_인어도_처형되면_인어_혼자_이긴다() {
        Game game = game(SIREN, RAIDER, MERMAID, SAILOR, SAILOR, SAILOR);
        seduce(game, 3L);
        game.getPlayer(3L).kill(DeathCause.EXECUTION);

        assertThat(winnerOf(game)).contains(Winner.MERMAID);
        assertThat(winnerIdsOf(game)).containsExactly(3L);
    }

    // ---------- 크라켄 ----------

    @Test
    void 해적을_뺀_생존자가_크라켄_포함_2명_이하면_크라켄이_해적보다_먼저_이긴다() {
        Game game = game(RAIDER, RAIDER, KRAKEN, SAILOR, SAILOR, SAILOR);
        kill(game, 4L, 5L); // 해적 2 : 나머지 2 → 해적 승리 조건도 충족

        assertThat(winnerOf(game)).contains(Winner.KRAKEN);
        assertThat(winnerIdsOf(game)).containsExactly(3L);
    }

    @Test
    void 크라켄이_살아_있으면_해적이_전멸해도_선원이_이기지_못한다() {
        Game game = game(RAIDER, KRAKEN, SAILOR, SAILOR, SAILOR, SAILOR);
        kill(game, 1L);
        assertThat(winnerOf(game)).isEmpty();

        kill(game, 2L);
        assertThat(winnerOf(game)).contains(Winner.CREW);
        assertThat(winnerIdsOf(game)).containsExactly(3L, 4L, 5L, 6L);
    }

    @Test
    void 해적이_전멸했으면_접선하지_않은_앵무새가_남아도_해적_팀은_이기지_못한다() {
        Game game = game(RAIDER, PARROT, KRAKEN, SAILOR, SAILOR, SAILOR);
        kill(game, 1L, 4L, 5L); // 앵무새·크라켄·선원 1 → 크라켄 조건(2명 이하) 미충족, 크라켄 때문에 선원도 못 이김

        assertThat(winnerOf(game)).isEmpty();
    }

    // ---------- 세이렌 팀 ----------

    @Test
    void 세이렌이_살아_있고_해적이_전멸했을_때_세이렌_팀이_선원보다_적지_않으면_세이렌_팀이_이긴다() {
        Game game = game(SIREN, RAIDER, SAILOR, SAILOR, SAILOR, SAILOR);
        seduce(game, 3L, 4L);
        kill(game, 2L, 4L); // 세이렌 팀 2(1, 3) : 선원 2(5, 6), 4번은 죽은 팀원

        assertThat(winnerOf(game)).contains(Winner.SIREN);
        assertThat(winnerIdsOf(game)).containsExactly(1L, 3L, 4L);
    }

    @Test
    void 세이렌이_살아_있어도_해적이_전멸했을_때_선원이_더_많으면_선원이_이긴다() {
        Game game = game(SIREN, RAIDER, SAILOR, SAILOR, SAILOR, SAILOR);
        seduce(game, 3L);
        kill(game, 2L); // 세이렌 팀 2 : 선원 3

        assertThat(winnerOf(game)).contains(Winner.CREW);
        assertThat(winnerIdsOf(game)).containsExactly(4L, 5L, 6L);
    }

    @Test
    void 세이렌이_살아_있고_선원이_전멸했을_때_세이렌_팀이_해적보다_많으면_세이렌_팀이_이긴다() {
        Game game = game(SIREN, RAIDER, SAILOR, SAILOR, SAILOR);
        seduce(game, 3L, 4L);
        kill(game, 5L); // 세이렌 팀 3 : 해적 1, 선원 0

        assertThat(winnerOf(game)).contains(Winner.SIREN);
    }

    @Test
    void 세이렌이_죽었으면_해적과_선원이_모두_전멸해야_세이렌_팀이_이긴다() {
        Game game = game(SIREN, RAIDER, SAILOR, SAILOR);
        seduce(game, 3L);
        kill(game, 1L, 2L, 4L);

        assertThat(winnerOf(game)).contains(Winner.SIREN);
        assertThat(winnerIdsOf(game)).containsExactly(1L, 3L);
    }

    @Test
    void 세이렌이_죽고_해적이_전멸했을_때_선원이_남아_있으면_선원이_이긴다() {
        Game game = game(SIREN, RAIDER, SAILOR, SAILOR, SAILOR);
        seduce(game, 3L, 4L);
        kill(game, 1L, 2L); // 유혹당한 팀원 2명이 남아도 선원 승리

        assertThat(winnerOf(game)).contains(Winner.CREW);
        assertThat(winnerIdsOf(game)).containsExactly(5L);
    }

    // ---------- 유령 선장 ----------

    @Test
    void 유령_선장이_살아서_사망자가_생존자보다_많아지면_해적보다_먼저_혼자_이긴다() {
        Game game = game(RAIDER, RAIDER, GHOST, SAILOR, SAILOR, SAILOR, SAILOR, SAILOR, SAILOR);
        kill(game, 4L, 5L, 6L, 7L, 8L); // 사망 5 > 생존 4, 해적 2 : 나머지 2

        assertThat(winnerOf(game)).contains(Winner.GHOST_CAPTAIN);
        assertThat(winnerIdsOf(game)).containsExactly(3L);
    }

    @Test
    void 유령_선장이_죽었으면_사망자가_많아도_이기지_못한다() {
        Game game = game(RAIDER, GHOST, SAILOR, SAILOR, SAILOR, SAILOR, SAILOR);
        game.getPlayer(2L).kill(DeathCause.EXECUTION);
        kill(game, 3L, 4L, 5L); // 사망 4 > 생존 3

        assertThat(winnerOf(game)).isEmpty();
    }

    // ---------- 우선순위 ----------

    @Test
    void 크라켄과_세이렌_조건이_함께_맞으면_크라켄이_이긴다() {
        Game game = game(KRAKEN, SIREN, RAIDER, SAILOR, SAILOR);
        kill(game, 3L, 4L, 5L);

        assertThat(winnerOf(game)).contains(Winner.KRAKEN);
    }

    @Test
    void 세이렌과_유령_선장_조건이_함께_맞으면_세이렌_팀이_이긴다() {
        Game game = game(SIREN, RAIDER, GHOST, SAILOR, SAILOR, SAILOR, SAILOR, SAILOR, SAILOR);
        seduce(game, 4L, 5L);
        kill(game, 2L, 6L, 7L, 8L, 9L); // 사망 5 > 생존 4, 해적 0, 세이렌 팀 3 >= 선원 0

        assertThat(winnerOf(game)).contains(Winner.SIREN);
    }

    // ---------- 전적 ----------

    @Test
    void 전적은_실제로_이긴_사람만_승리로_기록한다() {
        Game game = game(SIREN, RAIDER, SAILOR, SAILOR, SAILOR, SAILOR);
        seduce(game, 3L, 4L);
        kill(game, 2L, 4L);
        WinConditionChecker.Victory victory = checker.check(game).orElseThrow();

        game.end(victory.winner(), victory.winnerIds());
        GameEndedEvent event = GameEndedEvent.from(game);

        assertThat(event.winner()).isEqualTo(Winner.SIREN);
        assertThat(event.outcomes()).filteredOn(GameEndedEvent.PlayerOutcome::win)
                .extracting(GameEndedEvent.PlayerOutcome::userId)
                .containsExactly(1L, 3L, 4L);
    }
}
