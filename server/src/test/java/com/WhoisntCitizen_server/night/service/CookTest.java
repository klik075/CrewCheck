package com.WhoisntCitizen_server.night.service;

import com.WhoisntCitizen_server.common.exception.GameRuleException;
import com.WhoisntCitizen_server.game.entity.Game;
import com.WhoisntCitizen_server.game.entity.GamePhase;
import com.WhoisntCitizen_server.game.entity.GamePlayer;
import com.WhoisntCitizen_server.game.entity.Winner;
import com.WhoisntCitizen_server.game.service.WinConditionChecker;
import com.WhoisntCitizen_server.jobs.domain.ActionCode;
import com.WhoisntCitizen_server.jobs.domain.Faction;
import com.WhoisntCitizen_server.jobs.domain.RoleDefinition;
import com.WhoisntCitizen_server.night.entity.NightResult;
import com.WhoisntCitizen_server.night.entity.PrivateReport;
import org.junit.jupiter.api.Test;

import java.time.Instant;
import java.util.ArrayList;
import java.util.List;
import java.util.Random;

import static org.assertj.core.api.Assertions.assertThat;
import static org.assertj.core.api.Assertions.assertThatThrownBy;

/** 요리사: 밤에 1명을 골라 다음 투표를 못 하게 한다. 같은 대상 연속 불가. */
class CookTest {

    private static final RoleDefinition RAIDER =
            new RoleDefinition("PIRATE_RAIDER", "해적", Faction.PIRATE, ActionCode.SELECT_ATTACK_TARGET);
    private static final RoleDefinition COOK =
            new RoleDefinition("PIRATE_COOK", "요리사", Faction.PIRATE, ActionCode.BAN_VOTE);
    private static final RoleDefinition BOATSWAIN =
            new RoleDefinition("CREW_BOATSWAIN", "갑판장", Faction.CREW, ActionCode.BLOCK);
    private static final RoleDefinition SAILOR =
            new RoleDefinition("CREW_SAILOR", "선원", Faction.CREW, null);

    private final NightActionResolver resolver = new NightActionResolver(new Random(42));

    /** roles 순서대로 playerId 1, 2, 3... 을 붙여 첫 밤 상태의 게임을 만든다. */
    private static Game game(RoleDefinition... roles) {
        List<GamePlayer> players = new ArrayList<>();
        for (int i = 0; i < roles.length; i++) {
            players.add(new GamePlayer((long) (i + 1), "p" + (i + 1), roles[i]));
        }
        Game game = new Game("room-1", players);
        game.changePhase(GamePhase.NIGHT, Instant.now().plusSeconds(30));
        return game;
    }

    private static void phase(Game game, GamePhase next) {
        game.changePhase(next, Instant.now().plusSeconds(30));
    }

    // ---------- 투표 금지 ----------

    @Test
    void 요리사의_대상은_다음_투표를_할_수_없다() {
        Game game = game(RAIDER, COOK, SAILOR, SAILOR, SAILOR);
        game.recordNightAction(2L, 3L);
        resolver.resolve(game);

        phase(game, GamePhase.VOTE);

        assertThat(game.isVoteBanned(3L)).isTrue();
        assertThatThrownBy(() -> game.recordVote(3L, 1L))
                .isInstanceOf(GameRuleException.class)
                .hasMessageContaining("오늘은 투표할 수 없습니다");
    }

    @Test
    void 투표가_금지된_사람은_기다리지_않고_나머지가_모두_내면_끝난다() {
        Game game = game(RAIDER, COOK, SAILOR, SAILOR, SAILOR);
        game.recordNightAction(2L, 3L);
        resolver.resolve(game);
        phase(game, GamePhase.VOTE);

        assertThat(game.eligibleVoterCount()).isEqualTo(4);
        game.recordVote(1L, 4L);
        game.recordVote(2L, 4L);
        game.recordVote(4L, 1L);
        assertThat(game.allVotesSubmitted()).isFalse();
        game.recordVote(5L, 1L);

        assertThat(game.allVotesSubmitted()).isTrue();
    }

    @Test
    void 요리사는_대상을_대상은_본인만_투표_금지_결과를_받는다() {
        Game game = game(RAIDER, COOK, SAILOR, SAILOR, SAILOR);
        game.recordNightAction(2L, 3L);

        NightResult result = resolver.resolve(game);

        assertThat(result.reportsFor(2L)).containsExactly(PrivateReport.voteBan(3L));
        assertThat(result.reportsFor(3L)).containsExactly(PrivateReport.voteBanned());
        assertThat(result.reportsFor(3L).get(0).targetId()).isNull();
        assertThat(result.reportsFor(4L)).isEmpty();
    }

    @Test
    void 다음_밤이_되면_투표_금지가_풀린다() {
        Game game = game(RAIDER, COOK, SAILOR, SAILOR, SAILOR);
        game.recordNightAction(2L, 3L);
        resolver.resolve(game);
        phase(game, GamePhase.VOTE);

        phase(game, GamePhase.NIGHT);
        resolver.resolve(game); // 둘째 밤에는 아무도 고르지 않음
        phase(game, GamePhase.VOTE);

        assertThat(game.isVoteBanned(3L)).isFalse();
        assertThat(game.eligibleVoterCount()).isEqualTo(5);
        game.recordVote(3L, 1L);
    }

    @Test
    void 이번_밤에_죽은_대상은_안내를_받지_않는다() {
        Game game = game(RAIDER, COOK, SAILOR, SAILOR, SAILOR);
        game.recordNightAction(1L, 3L);
        game.recordNightAction(2L, 3L);

        NightResult result = resolver.resolve(game);
        phase(game, GamePhase.VOTE);

        assertThat(result.killedPlayerId()).isEqualTo(3L);
        assertThat(result.reportsFor(3L)).isEmpty();
        assertThat(game.eligibleVoterCount()).isEqualTo(4);
    }

    // ---------- 같은 대상 연속 금지 ----------

    @Test
    void 이틀_연속_같은_대상은_고를_수_없다() {
        Game game = game(RAIDER, COOK, SAILOR, SAILOR, SAILOR);
        game.recordNightAction(2L, 3L);
        resolver.resolve(game);
        phase(game, GamePhase.NIGHT);

        assertThatThrownBy(() -> game.recordNightAction(2L, 3L))
                .isInstanceOf(GameRuleException.class)
                .hasMessageContaining("어젯밤과 같은 사람");
        game.recordNightAction(2L, 4L);
    }

    @Test
    void 하룻밤_쉬면_같은_대상을_다시_고를_수_있다() {
        Game game = game(RAIDER, COOK, SAILOR, SAILOR, SAILOR);
        game.recordNightAction(2L, 3L);
        resolver.resolve(game);
        phase(game, GamePhase.NIGHT);
        game.skipNightAction(2L);
        resolver.resolve(game);
        phase(game, GamePhase.NIGHT);

        game.recordNightAction(2L, 3L);
    }

    @Test
    void 차단당한_밤은_기록되지_않아_다음_밤에_같은_대상을_고를_수_있다() {
        Game game = game(RAIDER, COOK, BOATSWAIN, SAILOR, SAILOR);
        game.recordNightAction(2L, 4L);
        game.recordNightAction(3L, 2L); // 요리사 차단

        NightResult result = resolver.resolve(game);
        assertThat(result.reportsFor(2L)).containsExactly(PrivateReport.blocked());
        assertThat(result.reportsFor(4L)).isEmpty();
        phase(game, GamePhase.VOTE);
        assertThat(game.isVoteBanned(4L)).isFalse();

        phase(game, GamePhase.NIGHT);
        game.recordNightAction(2L, 4L);
    }

    @Test
    void 자기_자신은_고를_수_없다() {
        Game game = game(RAIDER, COOK, SAILOR, SAILOR, SAILOR);

        assertThatThrownBy(() -> game.recordNightAction(2L, 2L)).isInstanceOf(GameRuleException.class);
    }

    // ---------- 해적 진영 ----------

    @Test
    void 요리사와_해적은_처음부터_서로_안다() {
        Game game = game(RAIDER, COOK, SAILOR, SAILOR, SAILOR);

        assertThat(game.knownPirateAllies(game.getPlayer(2L))).extracting(GamePlayer::getPlayerId).containsExactly(1L);
        assertThat(game.knownPirateAllies(game.getPlayer(1L))).extracting(GamePlayer::getPlayerId).containsExactly(2L);
    }

    @Test
    void 해적이_모두_죽어도_요리사가_살아_있으면_선원이_이기지_않는다() {
        Game game = game(RAIDER, COOK, SAILOR, SAILOR, SAILOR);
        game.getPlayer(1L).kill();

        assertThat(new WinConditionChecker().check(game)).isEmpty();

        game.getPlayer(2L).kill();
        assertThat(new WinConditionChecker().check(game)).map(WinConditionChecker.Victory::winner).contains(Winner.CREW);
    }
}
