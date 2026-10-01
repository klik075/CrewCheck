package com.WhoisntCitizen_server.night.service;

import com.WhoisntCitizen_server.common.exception.GameRuleException;
import com.WhoisntCitizen_server.game.dto.GameResultResponse;
import com.WhoisntCitizen_server.game.dto.MyRoleResponse;
import com.WhoisntCitizen_server.game.entity.Game;
import com.WhoisntCitizen_server.game.entity.GamePhase;
import com.WhoisntCitizen_server.game.entity.GamePlayer;
import com.WhoisntCitizen_server.jobs.domain.ActionCode;
import com.WhoisntCitizen_server.jobs.domain.Faction;
import com.WhoisntCitizen_server.jobs.domain.RoleDefinition;
import com.WhoisntCitizen_server.night.entity.NightResult;
import com.WhoisntCitizen_server.night.entity.PrivateReport;
import com.WhoisntCitizen_server.night.entity.PrivateReport.ObservedAction;
import com.WhoisntCitizen_server.night.entity.ReportType;
import org.junit.jupiter.api.Test;

import java.time.Instant;
import java.util.HashSet;
import java.util.List;
import java.util.Random;
import java.util.Set;

import static org.assertj.core.api.Assertions.assertThat;
import static org.assertj.core.api.Assertions.assertThatThrownBy;

/** 5단계: 원숭이. 위장 직업의 능력을 쓰지만 효과가 없고, 방문 흔적과 가짜 결과만 남는다. */
class MonkeyTest {

    private static final RoleDefinition RAIDER =
            new RoleDefinition("PIRATE_RAIDER", "해적", Faction.PIRATE, ActionCode.SELECT_ATTACK_TARGET);
    private static final RoleDefinition PARROT =
            new RoleDefinition("PIRATE_PARROT", "앵무새", Faction.PIRATE, ActionCode.WATCH_ACTION);
    private static final RoleDefinition CAPTAIN =
            new RoleDefinition("CREW_CAPTAIN", "선장", Faction.CREW, ActionCode.INVESTIGATE_FACTION);
    private static final RoleDefinition DOCTOR =
            new RoleDefinition("CREW_DOCTOR", "선의", Faction.CREW, ActionCode.PROTECT);
    private static final RoleDefinition LOOKOUT =
            new RoleDefinition("CREW_LOOKOUT", "망루지기", Faction.CREW, ActionCode.WATCH_VISITORS);
    private static final RoleDefinition BOATSWAIN =
            new RoleDefinition("CREW_BOATSWAIN", "갑판장", Faction.CREW, ActionCode.BLOCK);
    private static final RoleDefinition DRUNK =
            new RoleDefinition("CREW_DRUNK", "주정뱅이", Faction.CREW, ActionCode.READ_CORPSE_ROLE);
    private static final RoleDefinition MONKEY =
            new RoleDefinition("CREW_MONKEY", "원숭이", Faction.CREW, null);
    private static final RoleDefinition SAILOR =
            new RoleDefinition("CREW_SAILOR", "선원", Faction.CREW, null);

    private final NightActionResolver resolver = new NightActionResolver(new Random(42));

    private static GamePlayer player(long id, RoleDefinition role) {
        return new GamePlayer(id, "p" + id, role);
    }

    private static GamePlayer monkeyAs(long id, RoleDefinition disguise) {
        return new GamePlayer(id, "p" + id, MONKEY, disguise);
    }

    private static Game night(GamePlayer... players) {
        Game game = new Game("room-1", List.of(players));
        game.changePhase(GamePhase.NIGHT, Instant.now().plusSeconds(30));
        return game;
    }

    // ---------- 효과 없음 ----------

    @Test
    void 원숭이_선의의_보호는_효과가_없다() {
        Game game = night(player(1, RAIDER), monkeyAs(2, DOCTOR), player(3, SAILOR), player(4, SAILOR));
        game.recordNightAction(1L, 3L);
        game.recordNightAction(2L, 3L);

        NightResult result = resolver.resolve(game);

        assertThat(result.killedPlayerId()).isEqualTo(3L);
        assertThat(result.protectedByDoctor()).isFalse();
    }

    @Test
    void 원숭이_갑판장의_차단은_효과가_없다() {
        Game game = night(player(1, RAIDER), monkeyAs(2, BOATSWAIN), player(3, CAPTAIN), player(4, SAILOR));
        game.recordNightAction(2L, 3L); // 원숭이 → 선장 차단 시도
        game.recordNightAction(3L, 1L);

        NightResult result = resolver.resolve(game);

        assertThat(result.reportsFor(3L)).containsExactly(PrivateReport.faction(1L, Faction.PIRATE));
    }

    // ---------- 방문 흔적 ----------

    @Test
    void 원숭이의_방문은_망루지기에게_보인다() {
        Game game = night(player(1, RAIDER), monkeyAs(2, CAPTAIN), player(3, LOOKOUT), player(4, SAILOR));
        game.recordNightAction(2L, 4L);
        game.recordNightAction(3L, 4L);

        NightResult result = resolver.resolve(game);

        assertThat(result.reportsFor(3L)).containsExactly(PrivateReport.visitors(4L, List.of(2L)));
    }

    @Test
    void 원숭이의_위장_행동은_앵무새에게_보인다() {
        Game game = night(player(1, RAIDER), player(2, PARROT), monkeyAs(3, DOCTOR), player(4, SAILOR));
        game.recordNightAction(2L, 3L);
        game.recordNightAction(3L, 4L);

        NightResult result = resolver.resolve(game);

        assertThat(result.reportsFor(2L)).containsExactly(PrivateReport.actions(3L,
                List.of(new ObservedAction(ActionCode.PROTECT, 4L))));
    }

    // ---------- 가짜 결과 ----------

    @Test
    void 원숭이_선장은_무작위_진영을_받는다() {
        Set<Faction> seen = new HashSet<>();
        for (int i = 0; i < 30; i++) {
            Game game = night(player(1, RAIDER), monkeyAs(2, CAPTAIN), player(3, SAILOR), player(4, SAILOR));
            game.recordNightAction(2L, 3L);

            NightResult result = resolver.resolve(game); // 같은 Random을 이어 써서 여러 결과가 나오게 한다

            PrivateReport report = result.reportsFor(2L).get(0);
            assertThat(report.type()).isEqualTo(ReportType.FACTION);
            assertThat(report.targetId()).isEqualTo(3L);
            seen.add(report.faction());
        }
        assertThat(seen).containsExactlyInAnyOrder(Faction.CREW, Faction.PIRATE);
    }

    @Test
    void 원숭이_망루지기는_본인과_대상을_뺀_생존자_중_0에서_2명을_받는다() {
        Set<Integer> sizes = new HashSet<>();
        for (int i = 0; i < 30; i++) {
            Game game = night(player(1, RAIDER), monkeyAs(2, LOOKOUT), player(3, SAILOR),
                    player(4, SAILOR), player(5, SAILOR), player(6, SAILOR));
            game.getPlayer(6L).kill(); // 이미 죽은 사람은 후보가 아니다
            game.recordNightAction(2L, 3L);

            NightResult result = resolver.resolve(game); // 같은 Random을 이어 써서 여러 결과가 나오게 한다

            PrivateReport report = result.reportsFor(2L).get(0);
            assertThat(report.type()).isEqualTo(ReportType.VISITORS);
            assertThat(report.playerIds()).hasSizeLessThanOrEqualTo(2).isSorted();
            assertThat(report.playerIds()).isSubsetOf(1L, 4L, 5L);
            sizes.add(report.playerIds().size());
        }
        assertThat(sizes).contains(0, 2);
    }

    @Test
    void 원숭이_주정뱅이는_이번_게임에_배정된_직업_중_하나를_받고_횟수가_줄어든다() {
        Game game = night(player(1, RAIDER), monkeyAs(2, DRUNK), player(3, CAPTAIN), player(4, SAILOR));
        game.getPlayer(4L).kill();
        game.recordNightAction(2L, 4L);

        NightResult result = resolver.resolve(game);

        PrivateReport report = result.reportsFor(2L).get(0);
        assertThat(report.type()).isEqualTo(ReportType.CORPSE_ROLE);
        assertThat(report.targetId()).isEqualTo(4L);
        assertThat(report.roleCode()).isIn("PIRATE_RAIDER", "CREW_MONKEY", "CREW_CAPTAIN", "CREW_SAILOR");
        assertThat(game.getPlayer(2L).remainingUses(ActionCode.READ_CORPSE_ROLE)).isEqualTo(1);
    }

    @Test
    void 원숭이_선의_갑판장은_진짜처럼_결과가_없다() {
        Game game = night(player(1, RAIDER), monkeyAs(2, DOCTOR), monkeyAs(3, BOATSWAIN), player(4, SAILOR));
        game.recordNightAction(2L, 4L);
        game.recordNightAction(3L, 1L);

        NightResult result = resolver.resolve(game);

        assertThat(result.reportsFor(2L)).isEmpty();
        assertThat(result.reportsFor(3L)).isEmpty();
    }

    @Test
    void 차단당한_원숭이는_결과를_받지_못하고_횟수도_줄지_않는다() {
        Game game = night(player(1, RAIDER), monkeyAs(2, DRUNK), player(3, BOATSWAIN), player(4, SAILOR));
        game.getPlayer(4L).kill();
        game.recordNightAction(2L, 4L);
        game.recordNightAction(3L, 2L); // 진짜 갑판장 → 원숭이

        NightResult result = resolver.resolve(game);

        assertThat(result.reportsFor(2L)).isEmpty();
        assertThat(game.getPlayer(2L).remainingUses(ActionCode.READ_CORPSE_ROLE)).isEqualTo(2);
    }

    @Test
    void 원숭이는_이번_밤에_죽어도_결과를_받는다() {
        Game game = night(player(1, RAIDER), monkeyAs(2, CAPTAIN), player(3, SAILOR), player(4, SAILOR));
        game.recordNightAction(1L, 2L);
        game.recordNightAction(2L, 3L);

        NightResult result = resolver.resolve(game);

        assertThat(result.killedPlayerId()).isEqualTo(2L);
        assertThat(result.reportsFor(2L)).hasSize(1);
    }

    // ---------- 위장 직업 규칙 ----------

    @Test
    void 원숭이_주정뱅이도_살아_있는_대상은_고를_수_없다() {
        Game game = night(player(1, RAIDER), monkeyAs(2, DRUNK), player(3, SAILOR), player(4, SAILOR));

        assertThatThrownBy(() -> game.recordNightAction(2L, 3L))
                .isInstanceOf(GameRuleException.class)
                .hasMessageContaining("사망한 플레이어만");
    }

    @Test
    void 원숭이_선의도_이틀_연속으로_자신을_보호할_수_없다() {
        Game game = night(player(1, RAIDER), monkeyAs(2, DOCTOR), player(3, SAILOR), player(4, SAILOR));
        game.recordNightAction(2L, 2L);
        resolver.resolve(game);
        game.changePhase(GamePhase.NIGHT, Instant.now().plusSeconds(30));

        assertThatThrownBy(() -> game.recordNightAction(2L, 2L))
                .isInstanceOf(GameRuleException.class)
                .hasMessageContaining("이틀 연속");
    }

    @Test
    void 전원_제출_판정에서_원숭이는_위장_능력_기준으로_센다() {
        Game game = night(player(1, RAIDER), monkeyAs(2, CAPTAIN), monkeyAs(3, DRUNK), player(4, SAILOR));
        game.recordNightAction(1L, 4L);

        assertThat(game.allNightActionsSubmitted()).isFalse(); // 원숭이 선장은 제출해야 한다
        game.recordNightAction(2L, 4L);
        assertThat(game.allNightActionsSubmitted()).isTrue();  // 원숭이 주정뱅이는 시체가 없어 빠진다
    }

    // ---------- 보이는 직업 ----------

    @Test
    void 내_역할_조회에서는_위장_직업이_보이고_게임_결과에서는_원숭이로_공개된다() {
        GamePlayer monkey = monkeyAs(2, CAPTAIN);

        MyRoleResponse me = MyRoleResponse.of(monkey, List.of());
        GameResultResponse.PlayerResult result = GameResultResponse.PlayerResult.from(monkey);

        assertThat(me.role()).isEqualTo("CREW_CAPTAIN");
        assertThat(me.roleName()).isEqualTo("선장");
        assertThat(me.actionCode()).isEqualTo(ActionCode.INVESTIGATE_FACTION);
        assertThat(result.role()).isEqualTo("CREW_MONKEY");
    }
}