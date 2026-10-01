package com.WhoisntCitizen_server.night.service;

import com.WhoisntCitizen_server.game.entity.Game;
import com.WhoisntCitizen_server.game.entity.GamePhase;
import com.WhoisntCitizen_server.game.entity.GamePlayer;
import com.WhoisntCitizen_server.jobs.domain.ActionCode;
import com.WhoisntCitizen_server.jobs.domain.Faction;
import com.WhoisntCitizen_server.jobs.domain.RoleDefinition;
import com.WhoisntCitizen_server.night.entity.NightResult;
import com.WhoisntCitizen_server.night.entity.PrivateReport;
import org.junit.jupiter.api.BeforeEach;
import org.junit.jupiter.api.Test;

import java.time.Instant;
import java.util.List;
import java.util.Random;

import static org.assertj.core.api.Assertions.assertThat;

/** DB 없이 RoleDefinition을 직접 만들어 밤 판정만 검증한다. */
class NightActionResolverTest {

    private static final RoleDefinition RAIDER =
            new RoleDefinition("PIRATE_RAIDER", "해적", Faction.PIRATE, ActionCode.SELECT_ATTACK_TARGET);
    private static final RoleDefinition CAPTAIN =
            new RoleDefinition("CREW_CAPTAIN", "선장", Faction.CREW, ActionCode.INVESTIGATE_FACTION);
    private static final RoleDefinition DOCTOR =
            new RoleDefinition("CREW_DOCTOR", "선의", Faction.CREW, ActionCode.PROTECT);
    private static final RoleDefinition SAILOR =
            new RoleDefinition("CREW_SAILOR", "선원", Faction.CREW, null);

    // 1: 해적, 2: 해적, 3: 선장, 4: 선의, 5: 선원, 6: 선원
    private Game game;
    private NightActionResolver resolver;

    @BeforeEach
    void setUp() {
        game = new Game("room-1", List.of(
                new GamePlayer(1L, "해적1", RAIDER),
                new GamePlayer(2L, "해적2", RAIDER),
                new GamePlayer(3L, "선장", CAPTAIN),
                new GamePlayer(4L, "선의", DOCTOR),
                new GamePlayer(5L, "선원1", SAILOR),
                new GamePlayer(6L, "선원2", SAILOR)));
        game.changePhase(GamePhase.NIGHT, Instant.now().plusSeconds(30));
        resolver = new NightActionResolver(new Random(42));
    }

    @Test
    void 해적이_지목한_대상이_사망한다() {
        game.recordNightAction(1L, 5L);
        game.recordNightAction(2L, 5L);

        NightResult result = resolver.resolve(game);

        assertThat(result.killedPlayerId()).isEqualTo(5L);
        assertThat(result.protectedByDoctor()).isFalse();
        assertThat(game.getPlayer(5L).isAlive()).isFalse();
    }

    @Test
    void 선의가_보호하면_생존한다() {
        game.recordNightAction(1L, 5L);
        game.recordNightAction(2L, 5L);
        game.recordNightAction(4L, 5L);

        NightResult result = resolver.resolve(game);

        assertThat(result.killedPlayerId()).isNull();
        assertThat(result.protectedByDoctor()).isTrue();
        assertThat(game.getPlayer(5L).isAlive()).isTrue();
    }

    @Test
    void 해적_표가_동률이면_둘_중_한_명이_사망한다() {
        game.recordNightAction(1L, 5L);
        game.recordNightAction(2L, 6L);

        NightResult result = resolver.resolve(game);

        assertThat(result.killedPlayerId()).isIn(5L, 6L);
    }

    @Test
    void 판정_전에_다시_제출하면_마지막_제출이_적용된다() {
        game.recordNightAction(1L, 5L);
        game.recordNightAction(2L, 5L);
        game.recordNightAction(1L, 6L);
        game.recordNightAction(2L, 6L);

        NightResult result = resolver.resolve(game);

        assertThat(result.killedPlayerId()).isEqualTo(6L);
        assertThat(game.getPlayer(5L).isAlive()).isTrue();
    }

    @Test
    void 선장의_조사_결과는_선장_본인에게만_간다() {
        game.recordNightAction(3L, 1L);

        NightResult result = resolver.resolve(game);

        assertThat(result.reportsFor(3L)).containsExactly(PrivateReport.faction(1L, Faction.PIRATE));
        assertThat(result.reportsFor(4L)).isEmpty();
        assertThat(result.reportsFor(1L)).isEmpty();
    }

    @Test
    void 이번_밤에_죽은_선장도_조사_결과를_받는다() {
        game.recordNightAction(1L, 3L);
        game.recordNightAction(2L, 3L);
        game.recordNightAction(3L, 1L);

        NightResult result = resolver.resolve(game);

        assertThat(result.killedPlayerId()).isEqualTo(3L);
        assertThat(result.reportsFor(3L)).containsExactly(PrivateReport.faction(1L, Faction.PIRATE));
    }
}