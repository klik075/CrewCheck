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

/** 낮 토론 시간 연장: 살아 있는 사람마다 낮에 한 번 마감을 늦춘다. 페이즈 버전은 그대로다. */
class DayExtendTest {

    private static final RoleDefinition RAIDER =
            new RoleDefinition("PIRATE_RAIDER", "해적", Faction.PIRATE, ActionCode.SELECT_ATTACK_TARGET);
    private static final RoleDefinition SAILOR =
            new RoleDefinition("CREW_SAILOR", "선원", Faction.CREW, null);

    private static final Instant DAY_ENDS = Instant.parse("2026-10-01T12:01:00Z");

    // 1: 해적, 2~4: 선원
    private Game game;

    @BeforeEach
    void setUp() {
        game = new Game("room-1", List.of(
                new GamePlayer(1L, "해적", RAIDER),
                new GamePlayer(2L, "선원1", SAILOR),
                new GamePlayer(3L, "선원2", SAILOR),
                new GamePlayer(4L, "선원3", SAILOR)));
        game.changePhase(GamePhase.NIGHT, DAY_ENDS.minusSeconds(90));
        game.changePhase(GamePhase.DAY, DAY_ENDS);
    }

    @Test
    void 늘리면_마감이_늦춰지고_페이즈_버전은_그대로다() {
        long version = game.getPhaseVersion();

        assertThat(game.extendDay(2L, 10)).isTrue();

        assertThat(game.getPhaseEndsAt()).isEqualTo(DAY_ENDS.plusSeconds(10));
        assertThat(game.getPhaseVersion()).isEqualTo(version);
        assertThat(game.getPhase()).isEqualTo(GamePhase.DAY);
        assertThat(game.hasExtendedDay(2L)).isTrue();
        assertThat(game.dayExtendCount()).isEqualTo(1);
    }

    @Test
    void 같은_사람이_다시_늘리면_시간은_그대로다() {
        game.extendDay(2L, 10);

        assertThat(game.extendDay(2L, 10)).isFalse();

        assertThat(game.getPhaseEndsAt()).isEqualTo(DAY_ENDS.plusSeconds(10));
        assertThat(game.dayExtendCount()).isEqualTo(1);
    }

    @Test
    void 여러_사람이_늘리면_한_사람씩_더해진다() {
        game.extendDay(1L, 10);
        game.extendDay(3L, 10);
        game.extendDay(4L, 10);

        assertThat(game.getPhaseEndsAt()).isEqualTo(DAY_ENDS.plusSeconds(30));
        assertThat(game.dayExtendCount()).isEqualTo(3);
        assertThat(game.hasExtendedDay(2L)).isFalse();
    }

    @Test
    void 낮이_아니면_늘릴_수_없다() {
        game.changePhase(GamePhase.VOTE, DAY_ENDS.plusSeconds(30));
        Instant voteEnds = game.getPhaseEndsAt();

        assertThatThrownBy(() -> game.extendDay(1L, 10)).isInstanceOf(GameRuleException.class);
        assertThat(game.getPhaseEndsAt()).isEqualTo(voteEnds);
    }

    @Test
    void 사망자는_늘릴_수_없고_늘린_뒤_죽으면_인원에서_빠진다() {
        game.extendDay(3L, 10);
        game.getPlayer(4L).kill(DeathCause.EXECUTION);

        assertThatThrownBy(() -> game.extendDay(4L, 10)).isInstanceOf(GameRuleException.class);
        assertThat(game.getPhaseEndsAt()).isEqualTo(DAY_ENDS.plusSeconds(10));

        game.depart(3L); // 늘린 사람이 연결이 끊겨 사망 처리
        assertThat(game.dayExtendCount()).isZero();
        assertThat(game.getPhaseEndsAt()).isEqualTo(DAY_ENDS.plusSeconds(10)); // 이미 늘어난 시간은 그대로
    }

    @Test
    void 넘기기와_따로_센다() {
        game.skipDay(2L);

        assertThat(game.extendDay(2L, 10)).isTrue();   // 넘긴 사람도 늘릴 수 있다
        assertThat(game.daySkipCount()).isEqualTo(1);
        assertThat(game.dayExtendCount()).isEqualTo(1);

        game.extendDay(3L, 10);                         // 늘리기만 한 사람은 넘긴 것으로 세지 않는다
        assertThat(game.hasSkippedDay(3L)).isFalse();
        assertThat(game.daySkipCount()).isEqualTo(1);
    }

    @Test
    void 다음_낮이_되면_다시_늘릴_수_있다() {
        game.extendDay(1L, 10);
        game.changePhase(GamePhase.VOTE, DAY_ENDS.plusSeconds(30));
        game.changePhase(GamePhase.EXECUTION, DAY_ENDS.plusSeconds(60));
        game.changePhase(GamePhase.NIGHT, DAY_ENDS.plusSeconds(90));
        game.changePhase(GamePhase.NIGHT_RESULT, DAY_ENDS.plusSeconds(120));
        Instant nextDayEnds = DAY_ENDS.plusSeconds(180);
        game.changePhase(GamePhase.DAY, nextDayEnds);

        assertThat(game.hasExtendedDay(1L)).isFalse();
        assertThat(game.dayExtendCount()).isZero();
        assertThat(game.extendDay(1L, 10)).isTrue();
        assertThat(game.getPhaseEndsAt()).isEqualTo(nextDayEnds.plusSeconds(10));
    }
}
