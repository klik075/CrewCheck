package com.WhoisntCitizen_server.night.service;

import com.WhoisntCitizen_server.game.entity.DeathCause;
import com.WhoisntCitizen_server.game.entity.Game;
import com.WhoisntCitizen_server.game.entity.GamePhase;
import com.WhoisntCitizen_server.game.entity.GamePlayer;
import com.WhoisntCitizen_server.jobs.domain.ActionCode;
import com.WhoisntCitizen_server.jobs.domain.Faction;
import com.WhoisntCitizen_server.jobs.domain.RoleDefinition;
import com.WhoisntCitizen_server.night.entity.NightResult;
import com.WhoisntCitizen_server.vote.service.VoteResolver;
import org.junit.jupiter.api.Test;

import java.time.Instant;
import java.util.ArrayList;
import java.util.List;
import java.util.Random;

import static org.assertj.core.api.Assertions.assertThat;

/** 유령 선장: 능력 없음. 밤에는 죽지 않고(습격당해도 아무 일 없던 것처럼 공개), 투표로만 죽는다. */
class GhostCaptainTest {

    private static final RoleDefinition RAIDER =
            new RoleDefinition("PIRATE_RAIDER", "해적", Faction.PIRATE, ActionCode.SELECT_ATTACK_TARGET);
    private static final RoleDefinition GHOST =
            new RoleDefinition("NEUTRAL_GHOST_CAPTAIN", "유령 선장", Faction.NEUTRAL, null);
    private static final RoleDefinition DOCTOR =
            new RoleDefinition("CREW_DOCTOR", "선의", Faction.CREW, ActionCode.PROTECT);
    private static final RoleDefinition SAILOR =
            new RoleDefinition("CREW_SAILOR", "선원", Faction.CREW, null);

    private final NightActionResolver resolver = new NightActionResolver(new Random(42));

    private static Game game(RoleDefinition... roles) {
        List<GamePlayer> players = new ArrayList<>();
        for (int i = 0; i < roles.length; i++) {
            players.add(new GamePlayer((long) (i + 1), "p" + (i + 1), roles[i]));
        }
        Game game = new Game("room-1", players);
        game.changePhase(GamePhase.NIGHT, Instant.now().plusSeconds(30));
        return game;
    }

    @Test
    void 해적에게_습격당해도_죽지_않고_아무_일도_없던_것처럼_공개된다() {
        Game game = game(RAIDER, GHOST, DOCTOR, SAILOR, SAILOR);
        game.recordNightAction(1L, 2L);
        game.recordNightAction(3L, 2L); // 선의가 보호해도 "보호로 살았다"가 아니라 아무 일 없음

        NightResult result = resolver.resolve(game);

        assertThat(game.getPlayer(2L).isAlive()).isTrue();
        assertThat(result.killedPlayerId()).isNull();
        assertThat(result.deaths()).isEmpty();
        assertThat(result.protectedByDoctor()).isFalse();
    }

    @Test
    void 투표로는_처형된다() {
        Game game = game(RAIDER, GHOST, SAILOR, SAILOR, SAILOR);
        game.changePhase(GamePhase.VOTE, Instant.now().plusSeconds(30));
        game.recordVote(1L, 2L);
        game.recordVote(3L, 2L);

        new VoteResolver().resolve(game);

        assertThat(game.getPlayer(2L).isAlive()).isFalse();
        assertThat(game.getPlayer(2L).getDeathCause()).isEqualTo(DeathCause.EXECUTION);
    }
}
