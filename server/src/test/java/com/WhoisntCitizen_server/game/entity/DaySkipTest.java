package com.WhoisntCitizen_server.game.entity;

import com.WhoisntCitizen_server.common.exception.GameRuleException;
import com.WhoisntCitizen_server.jobs.domain.ActionCode;
import com.WhoisntCitizen_server.jobs.domain.Faction;
import com.WhoisntCitizen_server.jobs.domain.RoleDefinition;
import org.junit.jupiter.api.BeforeEach;
import org.junit.jupiter.api.Test;

import java.time.Instant;
import java.util.List;

import static org.assertj.core.api.Assertions.assertThat;
import static org.assertj.core.api.Assertions.assertThatThrownBy;

/** 낮 토론 넘기기: 살아 있는 전원이 넘기면 바로 투표로 넘어갈 수 있다. */
class DaySkipTest {

    private static final RoleDefinition RAIDER =
            new RoleDefinition("PIRATE_RAIDER", "해적", Faction.PIRATE, ActionCode.SELECT_ATTACK_TARGET);
    private static final RoleDefinition SAILOR =
            new RoleDefinition("CREW_SAILOR", "선원", Faction.CREW, null);

    // 1: 해적, 2~4: 선원
    private Game game;

    @BeforeEach
    void setUp() {
        game = new Game("room-1", List.of(
                new GamePlayer(1L, "해적", RAIDER),
                new GamePlayer(2L, "선원1", SAILOR),
                new GamePlayer(3L, "선원2", SAILOR),
                new GamePlayer(4L, "선원3", SAILOR)));
        game.changePhase(GamePhase.NIGHT, Instant.now().plusSeconds(30));
        game.changePhase(GamePhase.DAY, Instant.now().plusSeconds(30));
    }

    @Test
    void 전원이_넘겨야_끝난다() {
        game.skipDay(1L);
        game.skipDay(2L);
        game.skipDay(3L);
        assertThat(game.allDaySkipped()).isFalse();
        assertThat(game.daySkipCount()).isEqualTo(3);

        game.skipDay(4L);
        assertThat(game.allDaySkipped()).isTrue();
    }

    @Test
    void 같은_사람이_다시_넘기면_무시된다() {
        assertThat(game.skipDay(1L)).isTrue();
        assertThat(game.skipDay(1L)).isFalse();
        assertThat(game.daySkipCount()).isEqualTo(1);
    }

    @Test
    void 사망자는_넘길_수_없고_전원_수에서도_빠진다() {
        game.getPlayer(4L).kill(DeathCause.EXECUTION);

        assertThatThrownBy(() -> game.skipDay(4L)).isInstanceOf(GameRuleException.class);
        game.skipDay(1L);
        game.skipDay(2L);
        game.skipDay(3L);
        assertThat(game.allDaySkipped()).isTrue();
    }

    @Test
    void 연결이_끊긴_사람이_빠지면_남은_사람만으로_판단한다() {
        game.skipDay(1L);
        game.skipDay(2L);
        game.skipDay(3L);

        game.depart(4L);

        assertThat(game.allDaySkipped()).isTrue();
    }

    @Test
    void 낮이_아니면_넘길_수_없다() {
        game.changePhase(GamePhase.VOTE, Instant.now().plusSeconds(30));

        assertThatThrownBy(() -> game.skipDay(1L)).isInstanceOf(GameRuleException.class);
    }

    @Test
    void 다음_낮이_되면_다시_넘겨야_한다() {
        game.skipDay(1L);
        game.changePhase(GamePhase.VOTE, Instant.now().plusSeconds(30));
        game.changePhase(GamePhase.EXECUTION, Instant.now().plusSeconds(30));
        game.changePhase(GamePhase.NIGHT, Instant.now().plusSeconds(30));
        game.changePhase(GamePhase.NIGHT_RESULT, Instant.now().plusSeconds(30));
        game.changePhase(GamePhase.DAY, Instant.now().plusSeconds(30));

        assertThat(game.hasSkippedDay(1L)).isFalse();
        assertThat(game.daySkipCount()).isZero();
    }
}
