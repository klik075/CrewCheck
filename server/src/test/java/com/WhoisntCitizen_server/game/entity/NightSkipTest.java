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

/** 6단계: 밤 능력 넘기기(스킵). 마지막 선택이 이긴다. */
class NightSkipTest {

    private static final RoleDefinition RAIDER =
            new RoleDefinition("PIRATE_RAIDER", "해적", Faction.PIRATE, ActionCode.SELECT_ATTACK_TARGET);
    private static final RoleDefinition PARROT =
            new RoleDefinition("PIRATE_PARROT", "앵무새", Faction.PIRATE, ActionCode.WATCH_ACTION);
    private static final RoleDefinition CAPTAIN =
            new RoleDefinition("CREW_CAPTAIN", "선장", Faction.CREW, ActionCode.INVESTIGATE_FACTION);
    private static final RoleDefinition DOCTOR =
            new RoleDefinition("CREW_DOCTOR", "선의", Faction.CREW, ActionCode.PROTECT);
    private static final RoleDefinition SAILOR =
            new RoleDefinition("CREW_SAILOR", "선원", Faction.CREW, null);

    // 1: 해적, 2: 앵무새, 3: 선장, 4: 선의, 5~6: 선원
    private Game game;
    private NightActionResolver resolver;

    @BeforeEach
    void setUp() {
        game = new Game("room-1", List.of(
                new GamePlayer(1L, "해적", RAIDER),
                new GamePlayer(2L, "앵무새", PARROT),
                new GamePlayer(3L, "선장", CAPTAIN),
                new GamePlayer(4L, "선의", DOCTOR),
                new GamePlayer(5L, "선원1", SAILOR),
                new GamePlayer(6L, "선원2", SAILOR)));
        resolver = new NightActionResolver(new Random(42));
        nextNight();
    }

    private void nextNight() {
        game.changePhase(GamePhase.NIGHT, Instant.now().plusSeconds(30));
    }

    @Test
    void 넘긴_사람도_전원_제출에_포함된다() {
        game.recordNightAction(1L, 5L);
        game.skipNightAction(2L);
        game.skipNightAction(3L);

        assertThat(game.allNightActionsSubmitted()).isFalse();
        game.skipNightAction(4L);
        assertThat(game.allNightActionsSubmitted()).isTrue();
    }

    @Test
    void 제출했다가_넘기면_제출이_취소된다() {
        game.recordNightAction(1L, 5L);
        game.recordNightAction(4L, 5L); // 선의 보호
        game.skipNightAction(4L);       // 보호 취소

        NightResult result = resolver.resolve(game);

        assertThat(game.getNightActions()).doesNotContainKey(4L);
        assertThat(result.killedPlayerId()).isEqualTo(5L);
    }

    @Test
    void 넘겼다가_다시_제출하면_제출이_적용된다() {
        game.recordNightAction(1L, 5L);
        game.skipNightAction(4L);
        game.recordNightAction(4L, 5L);

        NightResult result = resolver.resolve(game);

        assertThat(result.killedPlayerId()).isNull();
        assertThat(result.protectedByDoctor()).isTrue();
    }

    @Test
    void 능력이_없는_직업은_넘길_수_없다() {
        assertThatThrownBy(() -> game.skipNightAction(5L))
                .isInstanceOf(GameRuleException.class)
                .hasMessageContaining("능력이 없는");
    }

    @Test
    void 접선한_앵무새는_넘길_수_없다() {
        game.recordNightAction(2L, 1L); // 앵무새 → 해적: 접선하고 행동 고정

        assertThatThrownBy(() -> game.skipNightAction(2L))
                .isInstanceOf(GameRuleException.class)
                .hasMessageContaining("확정");
    }

    @Test
    void 밤이_아니면_넘길_수_없다() {
        game.changePhase(GamePhase.DAY, Instant.now().plusSeconds(30));

        assertThatThrownBy(() -> game.skipNightAction(3L))
                .isInstanceOf(GameRuleException.class);
    }

    @Test
    void 넘긴_기록은_다음_밤에_초기화된다() {
        game.skipNightAction(1L);
        game.skipNightAction(2L);
        game.skipNightAction(3L);
        game.skipNightAction(4L);
        assertThat(game.allNightActionsSubmitted()).isTrue();

        resolver.resolve(game);
        nextNight();

        assertThat(game.allNightActionsSubmitted()).isFalse();
    }
}