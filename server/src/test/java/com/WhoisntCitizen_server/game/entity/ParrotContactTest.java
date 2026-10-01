package com.WhoisntCitizen_server.game.entity;

import com.WhoisntCitizen_server.common.exception.GameRuleException;
import com.WhoisntCitizen_server.jobs.domain.ActionCode;
import com.WhoisntCitizen_server.jobs.domain.Faction;
import com.WhoisntCitizen_server.jobs.domain.RoleDefinition;
import com.WhoisntCitizen_server.night.entity.NightResult;
import com.WhoisntCitizen_server.night.service.NightActionResolver;
import org.junit.jupiter.api.BeforeEach;
import org.junit.jupiter.api.Test;

import java.time.Instant;
import java.util.List;
import java.util.Random;

import static org.assertj.core.api.Assertions.assertThat;
import static org.assertj.core.api.Assertions.assertThatThrownBy;

/** 4단계: 앵무새 접선. 제출 즉시 접선하고, 접선한 밤에는 앵무새의 행동이 고정된다. */
class ParrotContactTest {

    private static final RoleDefinition RAIDER =
            new RoleDefinition("PIRATE_RAIDER", "해적", Faction.PIRATE, ActionCode.SELECT_ATTACK_TARGET);
    private static final RoleDefinition PARROT =
            new RoleDefinition("PIRATE_PARROT", "앵무새", Faction.PIRATE, ActionCode.WATCH_ACTION);
    private static final RoleDefinition BOATSWAIN =
            new RoleDefinition("CREW_BOATSWAIN", "갑판장", Faction.CREW, ActionCode.BLOCK);
    private static final RoleDefinition SAILOR =
            new RoleDefinition("CREW_SAILOR", "선원", Faction.CREW, null);

    private static final Instant NOW = Instant.parse("2026-10-01T12:00:00Z");

    // 1, 2: 해적, 3: 앵무새, 4: 갑판장, 5~6: 선원
    private Game game;
    private NightActionResolver resolver;

    @BeforeEach
    void setUp() {
        game = new Game("room-1", List.of(
                new GamePlayer(1L, "해적1", RAIDER),
                new GamePlayer(2L, "해적2", RAIDER),
                new GamePlayer(3L, "앵무새", PARROT),
                new GamePlayer(4L, "갑판장", BOATSWAIN),
                new GamePlayer(5L, "선원1", SAILOR),
                new GamePlayer(6L, "선원2", SAILOR)));
        resolver = new NightActionResolver(new Random(42));
        nextNight();
    }

    private void nextNight() {
        game.changePhase(GamePhase.NIGHT, Instant.now().plusSeconds(30));
    }

    private List<Long> alliesOf(Long playerId) {
        return game.knownPirateAllies(game.getPlayer(playerId)).stream().map(GamePlayer::getPlayerId).toList();
    }

    @Test
    void 접선_전에는_해적과_앵무새가_서로_모른다() {
        assertThat(alliesOf(1L)).containsExactly(2L);
        assertThat(alliesOf(3L)).isEmpty();
        assertThat(alliesOf(5L)).isEmpty();
    }

    @Test
    void 앵무새가_해적을_지목하면_제출_즉시_접선한다() {
        boolean contacted = game.recordNightAction(3L, 1L, NOW);

        assertThat(contacted).isTrue();
        assertThat(game.getPlayer(3L).isContacted()).isTrue();
        assertThat(game.getPlayer(3L).getContactedAt()).isEqualTo(NOW);
        assertThat(alliesOf(3L)).containsExactly(1L, 2L);
        assertThat(alliesOf(1L)).containsExactly(2L, 3L);
        assertThat(alliesOf(2L)).containsExactly(1L, 3L);
    }

    @Test
    void 앵무새가_선원을_지목하면_접선하지_않는다() {
        boolean contacted = game.recordNightAction(3L, 5L, NOW);

        assertThat(contacted).isFalse();
        assertThat(alliesOf(3L)).isEmpty();
    }

    @Test
    void 선원을_지목했다가_해적으로_바꾸면_그때_접선한다() {
        game.recordNightAction(3L, 5L, NOW);
        boolean contacted = game.recordNightAction(3L, 1L, NOW);

        assertThat(contacted).isTrue();
    }

    @Test
    void 접선한_밤에는_앵무새가_행동을_바꿀_수_없다() {
        game.recordNightAction(3L, 1L, NOW);

        assertThatThrownBy(() -> game.recordNightAction(3L, 5L, NOW))
                .isInstanceOf(GameRuleException.class)
                .hasMessageContaining("확정");
        assertThat(game.getNightActions().get(3L).targetId()).isEqualTo(1L);
    }

    @Test
    void 다음_밤에는_행동_고정이_풀리고_접선은_유지된다() {
        game.recordNightAction(3L, 1L, NOW);
        resolver.resolve(game);
        nextNight();

        boolean contactedAgain = game.recordNightAction(3L, 5L, NOW);

        assertThat(contactedAgain).isFalse();
        assertThat(game.getPlayer(3L).isContacted()).isTrue();
        assertThat(alliesOf(3L)).containsExactly(1L, 2L);
    }

    @Test
    void 이미_접선한_앵무새가_해적을_다시_지목해도_새로_접선하거나_고정되지_않는다() {
        game.recordNightAction(3L, 1L, NOW);
        resolver.resolve(game);
        nextNight();

        assertThat(game.recordNightAction(3L, 2L, NOW)).isFalse();
        game.recordNightAction(3L, 5L, NOW); // 고정되지 않았으므로 바꿀 수 있다
        assertThat(game.getNightActions().get(3L).targetId()).isEqualTo(5L);
    }

    @Test
    void 갑판장이_앵무새를_차단해도_접선은_유지된다() {
        game.recordNightAction(3L, 1L, NOW);
        game.recordNightAction(4L, 3L, NOW); // 갑판장 → 앵무새

        resolver.resolve(game);

        assertThat(game.getPlayer(3L).isContacted()).isTrue();
    }

    @Test
    void 접선한_앵무새도_해적의_공격_대상이_될_수_있다() {
        game.recordNightAction(3L, 1L, NOW);
        game.recordNightAction(1L, 3L, NOW);
        game.recordNightAction(2L, 3L, NOW);

        NightResult result = resolver.resolve(game);

        assertThat(result.killedPlayerId()).isEqualTo(3L);
        assertThat(game.getPlayer(3L).isContacted()).isTrue();
        assertThat(alliesOf(1L)).containsExactly(2L, 3L); // 죽은 동료도 목록에 남는다
    }
}