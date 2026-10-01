package com.WhoisntCitizen_server.game.entity;

import com.WhoisntCitizen_server.common.exception.GameRuleException;
import com.WhoisntCitizen_server.jobs.domain.ActionCode;
import com.WhoisntCitizen_server.jobs.domain.Faction;
import com.WhoisntCitizen_server.jobs.domain.RoleDefinition;
import com.WhoisntCitizen_server.night.entity.NightResult;
import com.WhoisntCitizen_server.night.entity.PrivateReport;
import com.WhoisntCitizen_server.night.service.NightActionResolver;
import org.junit.jupiter.api.BeforeEach;
import org.junit.jupiter.api.Test;

import java.time.Instant;
import java.util.List;
import java.util.Random;

import static org.assertj.core.api.Assertions.assertThat;
import static org.assertj.core.api.Assertions.assertThatThrownBy;

/** 2단계: 밤 행동 제출 검증, 판정 시점 사용 기록, 주정뱅이. */
class GameNightActionTest {

    private static final RoleDefinition RAIDER =
            new RoleDefinition("PIRATE_RAIDER", "해적", Faction.PIRATE, ActionCode.SELECT_ATTACK_TARGET);
    private static final RoleDefinition CAPTAIN =
            new RoleDefinition("CREW_CAPTAIN", "선장", Faction.CREW, ActionCode.INVESTIGATE_FACTION);
    private static final RoleDefinition DOCTOR =
            new RoleDefinition("CREW_DOCTOR", "선의", Faction.CREW, ActionCode.PROTECT);
    private static final RoleDefinition DRUNK =
            new RoleDefinition("CREW_DRUNK", "주정뱅이", Faction.CREW, ActionCode.READ_CORPSE_ROLE);
    private static final RoleDefinition SAILOR =
            new RoleDefinition("CREW_SAILOR", "선원", Faction.CREW, null);

    // 1: 해적, 2: 선장, 3: 선의, 4: 주정뱅이, 5~7: 선원
    private Game game;
    private NightActionResolver resolver;

    @BeforeEach
    void setUp() {
        game = new Game("room-1", List.of(
                new GamePlayer(1L, "해적", RAIDER),
                new GamePlayer(2L, "선장", CAPTAIN),
                new GamePlayer(3L, "선의", DOCTOR),
                new GamePlayer(4L, "주정뱅이", DRUNK),
                new GamePlayer(5L, "선원1", SAILOR),
                new GamePlayer(6L, "선원2", SAILOR),
                new GamePlayer(7L, "선원3", SAILOR)));
        resolver = new NightActionResolver(new Random(42));
        nextNight();
    }

    /** 밤 판정 후 다음 밤으로. changePhase(NIGHT)가 날짜를 올리고 제출 목록을 비운다. */
    private NightResult resolveAndNextNight() {
        NightResult result = resolver.resolve(game);
        nextNight();
        return result;
    }

    private void nextNight() {
        game.changePhase(GamePhase.NIGHT, Instant.now().plusSeconds(30));
    }

    // ---------- 대상 생존 규칙 (requiresLivingTarget) ----------

    @Test
    void 주정뱅이는_살아_있는_대상을_고를_수_없다() {
        assertThatThrownBy(() -> game.recordNightAction(4L, 5L))
                .isInstanceOf(GameRuleException.class)
                .hasMessageContaining("사망한 플레이어만");
    }

    @Test
    void 선장은_죽은_대상을_고를_수_없다() {
        game.getPlayer(5L).kill();

        assertThatThrownBy(() -> game.recordNightAction(2L, 5L))
                .isInstanceOf(GameRuleException.class)
                .hasMessageContaining("살아 있는 플레이어만");
    }

    // ---------- 주정뱅이 ----------

    @Test
    void 주정뱅이는_시체의_실제_직업을_본인만_받는다() {
        game.getPlayer(3L).kill(); // 선의 사망
        game.recordNightAction(4L, 3L);

        NightResult result = resolver.resolve(game);

        assertThat(result.reportsFor(4L)).containsExactly(PrivateReport.corpseRole(3L, DOCTOR));
        assertThat(result.reportsFor(2L)).isEmpty();
    }

    @Test
    void 주정뱅이는_세_번째_사용이_거부된다() {
        game.getPlayer(5L).kill();
        game.recordNightAction(4L, 5L);
        resolveAndNextNight();
        game.recordNightAction(4L, 5L);
        resolveAndNextNight();

        assertThat(game.getPlayer(4L).remainingUses(ActionCode.READ_CORPSE_ROLE)).isZero();
        assertThatThrownBy(() -> game.recordNightAction(4L, 5L))
                .isInstanceOf(GameRuleException.class)
                .hasMessageContaining("남은 사용 횟수");
    }

    @Test
    void 같은_밤에_대상을_여러_번_바꿔도_횟수는_한_번만_차감된다() {
        game.getPlayer(5L).kill();
        game.getPlayer(6L).kill();
        game.recordNightAction(4L, 5L);
        game.recordNightAction(4L, 6L);
        game.recordNightAction(4L, 5L);

        resolver.resolve(game);

        assertThat(game.getPlayer(4L).remainingUses(ActionCode.READ_CORPSE_ROLE)).isEqualTo(1);
    }

    @Test
    void 제출만_하고_판정_전이면_횟수가_차감되지_않는다() {
        game.getPlayer(5L).kill();
        game.recordNightAction(4L, 5L);

        assertThat(game.getPlayer(4L).remainingUses(ActionCode.READ_CORPSE_ROLE)).isEqualTo(2);
    }

    // ---------- 선의 연속 자기 보호 ----------

    @Test
    void 선의는_이틀_연속으로_자신을_보호할_수_없다() {
        game.recordNightAction(3L, 3L);
        resolveAndNextNight();

        assertThatThrownBy(() -> game.recordNightAction(3L, 3L))
                .isInstanceOf(GameRuleException.class)
                .hasMessageContaining("이틀 연속");
    }

    @Test
    void 자기_보호를_골랐다가_다른_대상으로_바꾸면_다음_밤에_자기_보호가_가능하다() {
        game.recordNightAction(3L, 3L);
        game.recordNightAction(3L, 5L);
        resolveAndNextNight();

        game.recordNightAction(3L, 3L); // 예외 없음
        assertThat(game.getNightActions().get(3L).targetId()).isEqualTo(3L);
    }

    @Test
    void 하루를_건너뛰면_다시_자기_보호가_가능하다() {
        game.recordNightAction(3L, 3L);
        resolveAndNextNight();
        game.recordNightAction(3L, 5L);
        resolveAndNextNight();

        game.recordNightAction(3L, 3L); // 예외 없음
        assertThat(game.getNightActions().get(3L).targetId()).isEqualTo(3L);
    }

    // ---------- 전원 제출 판정 ----------

    @Test
    void 시체가_없으면_주정뱅이는_전원_제출_인원에서_빠진다() {
        game.recordNightAction(1L, 5L);
        game.recordNightAction(2L, 1L);
        game.recordNightAction(3L, 5L);

        assertThat(game.allNightActionsSubmitted()).isTrue();
    }

    @Test
    void 시체가_있으면_주정뱅이도_제출해야_전원_제출이다() {
        game.getPlayer(7L).kill();
        game.recordNightAction(1L, 5L);
        game.recordNightAction(2L, 1L);
        game.recordNightAction(3L, 5L);

        assertThat(game.allNightActionsSubmitted()).isFalse();
        game.recordNightAction(4L, 7L);
        assertThat(game.allNightActionsSubmitted()).isTrue();
    }

    @Test
    void 횟수를_다_쓴_주정뱅이는_전원_제출_인원에서_빠진다() {
        game.getPlayer(7L).kill();
        game.recordNightAction(4L, 7L);
        resolveAndNextNight();
        game.recordNightAction(4L, 7L);
        resolveAndNextNight();

        game.recordNightAction(1L, 5L);
        game.recordNightAction(2L, 1L);
        game.recordNightAction(3L, 5L);

        assertThat(game.allNightActionsSubmitted()).isTrue();
    }
}