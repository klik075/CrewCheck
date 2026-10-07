package com.WhoisntCitizen_server.night.service;

import com.WhoisntCitizen_server.game.dto.MyRoleResponse;
import com.WhoisntCitizen_server.game.entity.Game;
import com.WhoisntCitizen_server.game.entity.GamePhase;
import com.WhoisntCitizen_server.game.entity.GamePlayer;
import com.WhoisntCitizen_server.game.entity.Team;
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

/** 세이렌: 선원 진영·인어를 유혹해 세이렌 팀으로 만든다. 성공한 다음 밤은 쉬고, 실패·차단·넘기기 다음 밤에는 다시 쓸 수 있다. */
class SirenTest {

    private static final RoleDefinition RAIDER =
            new RoleDefinition("PIRATE_RAIDER", "해적", Faction.PIRATE, ActionCode.SELECT_ATTACK_TARGET);
    private static final RoleDefinition SIREN =
            new RoleDefinition("NEUTRAL_SIREN", "세이렌", Faction.NEUTRAL, ActionCode.SEDUCE);
    private static final RoleDefinition KRAKEN =
            new RoleDefinition("NEUTRAL_KRAKEN", "크라켄", Faction.NEUTRAL, ActionCode.KRAKEN_MARK);
    private static final RoleDefinition GHOST =
            new RoleDefinition("NEUTRAL_GHOST_CAPTAIN", "유령 선장", Faction.NEUTRAL, null);
    private static final RoleDefinition MERMAID =
            new RoleDefinition("NEUTRAL_MERMAID", "인어", Faction.NEUTRAL, null);
    private static final RoleDefinition PARROT =
            new RoleDefinition("PIRATE_PARROT", "앵무새", Faction.PIRATE, ActionCode.WATCH_ACTION);
    private static final RoleDefinition CAPTAIN =
            new RoleDefinition("CREW_CAPTAIN", "선장", Faction.CREW, ActionCode.INVESTIGATE_FACTION);
    private static final RoleDefinition DOCTOR =
            new RoleDefinition("CREW_DOCTOR", "선의", Faction.CREW, ActionCode.PROTECT);
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

    // ---------- 유혹 ----------

    @Test
    void 선원을_유혹하면_직업은_그대로이고_세이렌_팀이_되며_서로_안다() {
        Game game = game(RAIDER, SIREN, CAPTAIN, SAILOR, SAILOR);
        game.recordNightAction(2L, 3L);

        NightResult result = resolver.resolve(game);

        GamePlayer captain = game.getPlayer(3L);
        assertThat(captain.getTeam()).isEqualTo(Team.SIREN);
        assertThat(captain.getRole()).isEqualTo(CAPTAIN);
        assertThat(result.reportsFor(2L)).containsExactly(PrivateReport.seduce(3L, true));
        assertThat(result.reportsFor(3L)).containsExactly(PrivateReport.seduced(List.of(2L)));
        assertThat(game.knownSirenTeam(game.getPlayer(2L))).extracting(GamePlayer::getPlayerId).containsExactly(3L);
        assertThat(game.knownSirenTeam(captain)).extracting(GamePlayer::getPlayerId).containsExactly(2L);
    }

    @Test
    void 인어도_유혹할_수_있다() {
        Game game = game(RAIDER, SIREN, MERMAID, SAILOR, SAILOR);
        game.recordNightAction(2L, 3L);

        NightResult result = resolver.resolve(game);

        assertThat(game.getPlayer(3L).getTeam()).isEqualTo(Team.SIREN);
        assertThat(result.reportsFor(2L)).containsExactly(PrivateReport.seduce(3L, true));
    }

    @Test
    void 해적_진영_크라켄_유령_선장은_유혹에_실패하고_세이렌에게_알린다() {
        for (RoleDefinition target : List.of(RAIDER, PARROT, KRAKEN, GHOST)) {
            Game game = game(SIREN, target, RAIDER, SAILOR, SAILOR);
            game.recordNightAction(1L, 2L);

            NightResult result = resolver.resolve(game);

            assertThat(result.reportsFor(1L)).as(target.code()).containsExactly(PrivateReport.seduce(2L, false));
            assertThat(result.reportsFor(2L)).as(target.code()).doesNotContain(PrivateReport.seduced(List.of(1L)));
            assertThat(game.getPlayer(2L).getTeam()).as(target.code()).isNotEqualTo(Team.SIREN);
        }
    }

    @Test
    void 선의의_보호로는_유혹을_막지_못한다() {
        Game game = game(RAIDER, SIREN, DOCTOR, SAILOR, SAILOR);
        game.recordNightAction(2L, 4L);
        game.recordNightAction(3L, 4L);

        resolver.resolve(game);

        assertThat(game.getPlayer(4L).getTeam()).isEqualTo(Team.SIREN);
    }

    @Test
    void 같은_밤에_해적에게_죽은_대상도_유혹은_성공한다() {
        Game game = game(RAIDER, SIREN, SAILOR, SAILOR, SAILOR);
        game.recordNightAction(2L, 3L);
        game.recordNightAction(1L, 3L);

        NightResult result = resolver.resolve(game);

        assertThat(game.getPlayer(3L).isAlive()).isFalse();
        assertThat(game.getPlayer(3L).getTeam()).isEqualTo(Team.SIREN);
        assertThat(result.reportsFor(2L)).containsExactly(PrivateReport.seduce(3L, true));
    }

    @Test
    void 선장이_조사하면_세이렌은_선원으로_보인다() {
        Game game = game(RAIDER, SIREN, CAPTAIN, SAILOR, SAILOR);
        game.recordNightAction(3L, 2L);

        NightResult result = resolver.resolve(game);

        assertThat(result.reportsFor(3L)).containsExactly(PrivateReport.faction(2L, Faction.CREW));
    }

    // ---------- 휴식 ----------

    @Test
    void 성공한_다음_밤에는_쉬고_그다음_밤에_다시_쓸_수_있다() {
        Game game = game(RAIDER, SIREN, SAILOR, SAILOR, SAILOR);
        game.recordNightAction(2L, 3L);
        resolver.resolve(game);
        phase(game, GamePhase.DAY);
        assertThat(MyRoleResponse.of(game, game.getPlayer(2L)).abilityAvailable()).isFalse();

        phase(game, GamePhase.NIGHT);
        assertThatThrownBy(() -> game.recordNightAction(2L, 4L)).hasMessageContaining("쉬어야 합니다");
        game.recordNightAction(1L, 5L);
        assertThat(game.allNightActionsSubmitted()).isTrue(); // 쉬는 세이렌은 기다리지 않는다
        resolver.resolve(game);

        phase(game, GamePhase.NIGHT);
        game.recordNightAction(2L, 4L);
    }

    @Test
    void 실패한_다음_밤에는_바로_다시_쓸_수_있다() {
        Game game = game(RAIDER, SIREN, SAILOR, SAILOR, SAILOR);
        game.recordNightAction(2L, 1L); // 해적: 실패
        resolver.resolve(game);
        phase(game, GamePhase.NIGHT);

        assertThat(MyRoleResponse.of(game, game.getPlayer(2L)).abilityAvailable()).isTrue();
        game.recordNightAction(2L, 3L);
    }

    @Test
    void 차단당한_다음_밤에는_바로_다시_쓸_수_있다() {
        Game game = game(RAIDER, SIREN, BOATSWAIN, SAILOR, SAILOR);
        game.recordNightAction(2L, 4L);
        game.recordNightAction(3L, 2L);

        NightResult result = resolver.resolve(game);
        assertThat(result.reportsFor(2L)).containsExactly(PrivateReport.blocked());
        assertThat(game.getPlayer(4L).getTeam()).isEqualTo(Team.CREW);

        phase(game, GamePhase.NIGHT);
        game.recordNightAction(2L, 4L);
    }
}
