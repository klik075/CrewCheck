package com.WhoisntCitizen_server.night.service;

import com.WhoisntCitizen_server.common.exception.GameRuleException;
import com.WhoisntCitizen_server.game.entity.DeathCause;
import com.WhoisntCitizen_server.game.entity.Game;
import com.WhoisntCitizen_server.game.entity.GamePhase;
import com.WhoisntCitizen_server.game.entity.GamePlayer;
import com.WhoisntCitizen_server.jobs.domain.ActionCode;
import com.WhoisntCitizen_server.jobs.domain.Faction;
import com.WhoisntCitizen_server.jobs.domain.RoleDefinition;
import com.WhoisntCitizen_server.night.entity.NightResult;
import com.WhoisntCitizen_server.night.entity.PrivateReport;
import com.WhoisntCitizen_server.night.entity.PrivateReport.ObservedAction;
import org.junit.jupiter.api.Test;

import java.time.Instant;
import java.util.ArrayList;
import java.util.List;
import java.util.Random;

import static org.assertj.core.api.Assertions.assertThat;
import static org.assertj.core.api.Assertions.assertThatThrownBy;

/** 크라켄: 매일 밤 표식(방문) 또는 발동(표식된 생존자 모두 처치, 방문 아님). 발동하면 표식을 지운다. */
class KrakenTest {

    private static final RoleDefinition RAIDER =
            new RoleDefinition("PIRATE_RAIDER", "해적", Faction.PIRATE, ActionCode.SELECT_ATTACK_TARGET);
    private static final RoleDefinition KRAKEN =
            new RoleDefinition("NEUTRAL_KRAKEN", "크라켄", Faction.NEUTRAL, ActionCode.KRAKEN_MARK);
    private static final RoleDefinition GHOST =
            new RoleDefinition("NEUTRAL_GHOST_CAPTAIN", "유령 선장", Faction.NEUTRAL, null);
    private static final RoleDefinition DOCTOR =
            new RoleDefinition("CREW_DOCTOR", "선의", Faction.CREW, ActionCode.PROTECT);
    private static final RoleDefinition BOATSWAIN =
            new RoleDefinition("CREW_BOATSWAIN", "갑판장", Faction.CREW, ActionCode.BLOCK);
    private static final RoleDefinition LOOKOUT =
            new RoleDefinition("CREW_LOOKOUT", "망루지기", Faction.CREW, ActionCode.WATCH_VISITORS);
    private static final RoleDefinition PARROT =
            new RoleDefinition("PIRATE_PARROT", "앵무새", Faction.PIRATE, ActionCode.WATCH_ACTION);
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

    private static void nextNight(Game game) {
        game.changePhase(GamePhase.NIGHT, Instant.now().plusSeconds(30));
    }

    private static void strike(Game game, long krakenId) {
        game.recordNightAction(krakenId, ActionCode.KRAKEN_STRIKE, null, Instant.now());
    }

    // ---------- 표식 ----------

    @Test
    void 표식을_남기면_크라켄과_대상_본인만_결과를_받는다() {
        Game game = game(RAIDER, KRAKEN, SAILOR, SAILOR, SAILOR);
        game.recordNightAction(2L, 3L);

        NightResult result = resolver.resolve(game);

        assertThat(result.deaths()).isEmpty();
        assertThat(result.reportsFor(2L)).containsExactly(PrivateReport.krakenMark(3L));
        assertThat(result.reportsFor(3L)).containsExactly(PrivateReport.krakenMarked());
        assertThat(result.reportsFor(4L)).isEmpty();
        assertThat(game.aliveKrakenMarks(game.getPlayer(2L))).containsExactly(3L);
    }

    @Test
    void 이미_표식을_남긴_사람과_자기_자신에게는_표식을_남길_수_없다() {
        Game game = game(RAIDER, KRAKEN, SAILOR, SAILOR, SAILOR);
        game.recordNightAction(2L, 3L);
        resolver.resolve(game);
        nextNight(game);

        assertThatThrownBy(() -> game.recordNightAction(2L, 3L)).hasMessageContaining("이미 표식");
        assertThatThrownBy(() -> game.recordNightAction(2L, 2L)).isInstanceOf(GameRuleException.class);
    }

    @Test
    void 표식은_방문으로_망루지기와_앵무새에게_보인다() {
        Game game = game(RAIDER, KRAKEN, LOOKOUT, PARROT, SAILOR);
        game.recordNightAction(2L, 5L);
        game.recordNightAction(3L, 5L); // 망루지기: 5번을 방문한 사람
        game.recordNightAction(4L, 2L); // 앵무새: 크라켄의 행동

        NightResult result = resolver.resolve(game);

        assertThat(result.reportsFor(3L)).containsExactly(PrivateReport.visitors(5L, List.of(2L)));
        assertThat(result.reportsFor(4L)).containsExactly(
                PrivateReport.actions(2L, List.of(new ObservedAction(ActionCode.KRAKEN_MARK, 5L))));
    }

    // ---------- 발동 ----------

    @Test
    void 발동하면_표식된_생존자를_모두_처치하고_표식을_지운다() {
        Game game = game(RAIDER, KRAKEN, SAILOR, SAILOR, SAILOR, SAILOR);
        game.recordNightAction(2L, 3L);
        resolver.resolve(game);
        nextNight(game);
        game.recordNightAction(2L, 4L);
        resolver.resolve(game);
        nextNight(game);

        strike(game, 2L);
        NightResult result = resolver.resolve(game);

        assertThat(result.deaths()).containsExactly(
                new NightResult.Death(3L, DeathCause.KRAKEN), new NightResult.Death(4L, DeathCause.KRAKEN));
        assertThat(result.killedPlayerId()).isNull(); // 해적의 습격이 아니다
        assertThat(game.getPlayer(3L).getDeathCause()).isEqualTo(DeathCause.KRAKEN);
        assertThat(game.getPlayer(2L).getKrakenMarks()).isEmpty();
    }

    @Test
    void 표식이_없으면_발동할_수_없고_크라켄이_아니면_발동을_고를_수_없다() {
        Game game = game(RAIDER, KRAKEN, SAILOR, SAILOR, SAILOR);

        assertThatThrownBy(() -> strike(game, 2L)).hasMessageContaining("표식을 남긴 살아 있는 사람이 없습니다");
        assertThatThrownBy(() -> strike(game, 1L)).hasMessageContaining("쓸 수 없는 능력");
    }

    @Test
    void 발동은_방문이_아니라_망루지기에게_보이지_않는다() {
        Game game = game(RAIDER, KRAKEN, LOOKOUT, SAILOR, SAILOR);
        game.recordNightAction(2L, 4L);
        resolver.resolve(game);
        nextNight(game);

        strike(game, 2L);
        game.recordNightAction(3L, 4L);
        NightResult result = resolver.resolve(game);

        assertThat(result.deaths()).containsExactly(new NightResult.Death(4L, DeathCause.KRAKEN));
        assertThat(result.reportsFor(3L)).containsExactly(PrivateReport.visitors(4L, List.of()));
    }

    @Test
    void 선의가_보호한_사람은_발동에서_살아남는다() {
        Game game = game(RAIDER, KRAKEN, DOCTOR, SAILOR, SAILOR);
        game.recordNightAction(2L, 4L);
        resolver.resolve(game);
        nextNight(game);
        game.recordNightAction(2L, 5L);
        resolver.resolve(game);
        nextNight(game);

        strike(game, 2L);
        game.recordNightAction(3L, 4L); // 선의가 4번 보호
        NightResult result = resolver.resolve(game);

        assertThat(result.deaths()).containsExactly(new NightResult.Death(5L, DeathCause.KRAKEN));
        assertThat(result.protectedByDoctor()).isTrue();
        assertThat(game.getPlayer(4L).isAlive()).isTrue();
    }

    @Test
    void 갑판장이_막으면_표식도_발동도_취소된다() {
        Game game = game(RAIDER, KRAKEN, BOATSWAIN, SAILOR, SAILOR);
        game.recordNightAction(2L, 4L);
        game.recordNightAction(3L, 2L); // 표식 차단
        NightResult first = resolver.resolve(game);
        assertThat(first.reportsFor(2L)).containsExactly(PrivateReport.blocked());
        assertThat(game.getPlayer(2L).getKrakenMarks()).isEmpty();

        nextNight(game);
        game.recordNightAction(2L, 4L);
        resolver.resolve(game);
        nextNight(game);
        strike(game, 2L);
        game.recordNightAction(3L, 2L); // 발동 차단
        NightResult third = resolver.resolve(game);

        assertThat(third.deaths()).isEmpty();
        assertThat(game.getPlayer(2L).getKrakenMarks()).containsExactly(4L); // 표식은 남는다
    }

    @Test
    void 유령_선장은_발동으로_죽지_않는다() {
        Game game = game(RAIDER, KRAKEN, GHOST, SAILOR, SAILOR);
        game.recordNightAction(2L, 3L);
        resolver.resolve(game);
        nextNight(game);

        strike(game, 2L);
        NightResult result = resolver.resolve(game);

        assertThat(result.deaths()).isEmpty();
        assertThat(result.protectedByDoctor()).isFalse();
        assertThat(game.getPlayer(3L).isAlive()).isTrue();
    }

    @Test
    void 해적의_습격과_발동으로_한_밤에_여러_명이_죽고_크라켄도_해적에게_죽는다() {
        Game game = game(RAIDER, KRAKEN, SAILOR, SAILOR, SAILOR);
        game.recordNightAction(2L, 4L);
        resolver.resolve(game);
        nextNight(game);

        strike(game, 2L);
        game.recordNightAction(1L, 2L); // 해적이 크라켄을 습격
        NightResult result = resolver.resolve(game);

        assertThat(result.deaths()).containsExactly(
                new NightResult.Death(2L, DeathCause.ATTACK), new NightResult.Death(4L, DeathCause.KRAKEN));
        assertThat(result.killedPlayerId()).isEqualTo(2L);
    }
}
