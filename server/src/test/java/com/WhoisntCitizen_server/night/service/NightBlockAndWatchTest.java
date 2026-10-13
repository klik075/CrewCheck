package com.WhoisntCitizen_server.night.service;

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

/** 3단계: 갑판장(차단), 망루지기(방문자), 앵무새(행동 관찰). */
class NightBlockAndWatchTest {

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

    // ---------- 갑판장 ----------

    @Test
    void 해적_1명이_차단되면_남은_해적의_표로만_공격한다() {
        Game game = game(RAIDER, RAIDER, BOATSWAIN, SAILOR, SAILOR);
        game.recordNightAction(1L, 4L);
        game.recordNightAction(2L, 5L);
        game.recordNightAction(3L, 1L); // 1번 해적 차단

        NightResult result = resolver.resolve(game);

        assertThat(result.killedPlayerId()).isEqualTo(5L);
        assertThat(game.getPlayer(4L).isAlive()).isTrue();
    }

    @Test
    void 해적이_모두_차단되면_공격이_없다() {
        Game game = game(RAIDER, BOATSWAIN, SAILOR, SAILOR);
        game.recordNightAction(1L, 3L);
        game.recordNightAction(2L, 1L);

        NightResult result = resolver.resolve(game);

        assertThat(result.killedPlayerId()).isNull();
        assertThat(result.protectedByDoctor()).isFalse();
    }

    @Test
    void 선의가_차단되면_보호가_무효다() {
        Game game = game(RAIDER, DOCTOR, BOATSWAIN, SAILOR);
        game.recordNightAction(1L, 4L);
        game.recordNightAction(2L, 4L);
        game.recordNightAction(3L, 2L); // 선의 차단

        NightResult result = resolver.resolve(game);

        assertThat(result.killedPlayerId()).isEqualTo(4L);
        assertThat(result.protectedByDoctor()).isFalse();
    }

    @Test
    void 차단당한_선장은_조사_결과_대신_차단_안내를_받는다() {
        Game game = game(RAIDER, CAPTAIN, BOATSWAIN, SAILOR);
        game.recordNightAction(2L, 1L);
        game.recordNightAction(3L, 2L);

        NightResult result = resolver.resolve(game);

        assertThat(result.reportsFor(2L)).containsExactly(PrivateReport.blocked());
        assertThat(result.reportsFor(3L)).containsExactly(PrivateReport.block(2L)); // 갑판장은 차단 결과
    }

    @Test
    void 차단당한_해적과_선의도_본인에게만_차단_안내를_받는다() {
        Game game = game(RAIDER, DOCTOR, BOATSWAIN, BOATSWAIN, SAILOR);
        game.recordNightAction(1L, 5L);
        game.recordNightAction(2L, 5L);
        game.recordNightAction(3L, 1L); // 해적 차단
        game.recordNightAction(4L, 2L); // 선의 차단

        NightResult result = resolver.resolve(game);

        assertThat(result.killedPlayerId()).isNull();
        assertThat(result.reportsFor(1L)).containsExactly(PrivateReport.blocked());
        assertThat(result.reportsFor(2L)).containsExactly(PrivateReport.blocked());
        assertThat(result.reportsFor(5L)).isEmpty();
    }

    @Test
    void 능력을_넘긴_사람은_차단당해도_안내를_받지_않는다() {
        Game game = game(RAIDER, CAPTAIN, BOATSWAIN, SAILOR);
        game.recordNightAction(1L, 4L);
        game.skipNightAction(2L);
        game.recordNightAction(3L, 2L);

        NightResult result = resolver.resolve(game);

        assertThat(result.reportsFor(2L)).isEmpty();
    }

    @Test
    void 갑판장끼리_서로_차단해도_둘_다의_차단이_적용된다() {
        Game game = game(RAIDER, BOATSWAIN, BOATSWAIN, CAPTAIN, SAILOR);
        game.recordNightAction(2L, 3L);
        game.recordNightAction(3L, 4L); // 차단당한 갑판장의 차단도 유효
        game.recordNightAction(4L, 1L);

        NightResult result = resolver.resolve(game);

        assertThat(result.reportsFor(4L)).containsExactly(PrivateReport.blocked());
        assertThat(result.reportsFor(2L)).containsExactly(PrivateReport.block(3L)); // 차단당한 갑판장의 차단도 결과를 받는다
        assertThat(result.reportsFor(3L)).containsExactly(PrivateReport.block(4L));
    }

    @Test
    void 차단당하면_주정뱅이의_사용_횟수가_차감되지_않는다() {
        Game game = game(RAIDER, DRUNK, BOATSWAIN, SAILOR, SAILOR);
        game.getPlayer(5L).kill();
        game.recordNightAction(2L, 5L);
        game.recordNightAction(3L, 2L);

        NightResult result = resolver.resolve(game);

        assertThat(result.reportsFor(2L)).containsExactly(PrivateReport.blocked());
        assertThat(game.getPlayer(2L).remainingUses(ActionCode.READ_CORPSE_ROLE)).isEqualTo(2);
    }

    @Test
    void 차단당한_선의는_다음_밤에_다시_자기_보호를_할_수_있다() {
        Game game = game(RAIDER, DOCTOR, BOATSWAIN, SAILOR);
        game.recordNightAction(2L, 2L); // 선의 자기 보호
        game.recordNightAction(3L, 2L); // 선의 차단
        resolver.resolve(game);
        game.changePhase(GamePhase.NIGHT, Instant.now().plusSeconds(30));

        game.recordNightAction(2L, 2L); // 예외 없음
        assertThat(game.getNightActions().get(2L).targetId()).isEqualTo(2L);
    }

    // ---------- 망루지기 ----------

    @Test
    void 망루지기는_공격_실행자_1명과_다른_방문자를_본다() {
        Game game = game(RAIDER, RAIDER, DOCTOR, LOOKOUT, SAILOR);
        game.recordNightAction(1L, 5L);
        game.recordNightAction(2L, 5L);
        game.recordNightAction(3L, 5L);
        game.recordNightAction(4L, 5L);

        NightResult result = resolver.resolve(game);

        PrivateReport report = result.reportsFor(4L).get(0);
        assertThat(report.playerIds()).hasSize(2).contains(3L);        // 선의 + 해적 1명
        assertThat(report.playerIds()).containsAnyOf(1L, 2L);
        assertThat(report.playerIds()).doesNotContain(4L);             // 본인 제외
    }

    @Test
    void 망루지기_결과에서_대상의_자기_방문은_빠진다() {
        Game game = game(RAIDER, DOCTOR, LOOKOUT, SAILOR);
        game.recordNightAction(2L, 2L); // 선의 자기 보호
        game.recordNightAction(3L, 2L);

        NightResult result = resolver.resolve(game);

        assertThat(result.reportsFor(3L)).containsExactly(PrivateReport.visitors(2L, List.of()));
    }

    @Test
    void 차단당한_플레이어의_방문은_보이지_않는다() {
        Game game = game(RAIDER, CAPTAIN, BOATSWAIN, LOOKOUT, SAILOR);
        game.recordNightAction(2L, 5L); // 선장 → 5 (차단됨)
        game.recordNightAction(3L, 2L); // 갑판장 → 선장
        game.recordNightAction(4L, 5L); // 망루지기 → 5

        NightResult result = resolver.resolve(game);

        assertThat(result.reportsFor(4L)).containsExactly(PrivateReport.visitors(5L, List.of()));
    }

    @Test
    void 망루지기는_이번_밤에_죽어도_결과를_받는다() {
        Game game = game(RAIDER, LOOKOUT, CAPTAIN, SAILOR);
        game.recordNightAction(1L, 2L);
        game.recordNightAction(2L, 4L);
        game.recordNightAction(3L, 4L);

        NightResult result = resolver.resolve(game);

        assertThat(result.killedPlayerId()).isEqualTo(2L);
        assertThat(result.reportsFor(2L)).containsExactly(PrivateReport.visitors(4L, List.of(3L)));
    }

    // ---------- 앵무새 ----------

    @Test
    void 앵무새는_대상이_한_행동을_본다() {
        Game game = game(RAIDER, PARROT, CAPTAIN, SAILOR);
        game.recordNightAction(2L, 3L);
        game.recordNightAction(3L, 4L);

        NightResult result = resolver.resolve(game);

        assertThat(result.reportsFor(2L)).containsExactly(PrivateReport.actions(3L,
                List.of(new ObservedAction(ActionCode.INVESTIGATE_FACTION, 4L))));
    }

    @Test
    void 앵무새는_차단된_행동은_보지_못하고_갑판장의_차단은_본다() {
        Game game = game(RAIDER, PARROT, PARROT, CAPTAIN, BOATSWAIN, SAILOR);
        game.recordNightAction(2L, 4L); // 앵무새A → 선장
        game.recordNightAction(3L, 5L); // 앵무새B → 갑판장
        game.recordNightAction(4L, 6L); // 선장 → 6 (차단됨)
        game.recordNightAction(5L, 4L); // 갑판장 → 선장

        NightResult result = resolver.resolve(game);

        assertThat(result.reportsFor(2L)).containsExactly(PrivateReport.actions(4L, List.of()));
        assertThat(result.reportsFor(3L)).containsExactly(PrivateReport.actions(5L,
                List.of(new ObservedAction(ActionCode.BLOCK, 4L))));
    }

    @Test
    void 앵무새가_해적을_관찰하면_그_해적의_행동_결과는_받지_않는다() {
        Game game = game(RAIDER, PARROT, SAILOR, SAILOR);
        game.recordNightAction(1L, 3L);
        game.recordNightAction(2L, 1L); // 접선(4단계)으로 대신한다

        NightResult result = resolver.resolve(game);

        assertThat(result.reportsFor(2L)).isEmpty();
    }

    // ---------- 전원 제출 판정 ----------

    @Test
    void 갑판장_망루지기_앵무새도_제출해야_전원_제출이다() {
        Game game = game(RAIDER, PARROT, LOOKOUT, BOATSWAIN, SAILOR);
        game.recordNightAction(1L, 5L);
        game.recordNightAction(2L, 5L);
        game.recordNightAction(3L, 5L);

        assertThat(game.allNightActionsSubmitted()).isFalse();
        game.recordNightAction(4L, 1L);
        assertThat(game.allNightActionsSubmitted()).isTrue();
    }
}