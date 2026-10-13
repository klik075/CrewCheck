package com.WhoisntCitizen_server.game.service;

import com.WhoisntCitizen_server.common.exception.GameRuleException;
import com.WhoisntCitizen_server.game.dto.RoleComposition;
import com.WhoisntCitizen_server.game.dto.RoleSetup;
import com.WhoisntCitizen_server.game.dto.RoleSetupMode;
import com.WhoisntCitizen_server.jobs.domain.RoleDefinition;
import com.WhoisntCitizen_server.support.TestRoles;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.params.ParameterizedTest;
import org.junit.jupiter.params.provider.ValueSource;

import java.util.HashSet;
import java.util.List;
import java.util.Set;
import java.util.stream.IntStream;

import static org.assertj.core.api.Assertions.assertThat;
import static org.assertj.core.api.Assertions.assertThatThrownBy;

/** 방의 직업 배정 설정: 추천 / 커스텀(인원수별 표) / 랜덤(진영 수 고정 + 후보) */
class RoleAssignerSetupTest {

    private static final List<String> ALL_SPECIALS = List.of(
            "PIRATE_PARROT", "CREW_CAPTAIN", "CREW_DOCTOR", "CREW_LOOKOUT", "CREW_MONKEY", "CREW_BOATSWAIN", "CREW_DRUNK");

    private final RoleAssigner assigner = TestRoles.assigner(7);

    private static RoleSetup custom(int playerCount, String... roles) {
        return new RoleSetup(RoleSetupMode.CUSTOM, List.of(new RoleComposition(playerCount, List.of(roles))), null);
    }

    private static RoleSetup random(List<String> candidates) {
        return new RoleSetup(RoleSetupMode.RANDOM, List.of(), candidates);
    }

    private static List<String> sorted(List<RoleDefinition> roles) {
        return roles.stream().map(RoleDefinition::code).sorted().toList();
    }

    private static List<String> sortedCodes(String... codes) {
        return List.of(codes).stream().sorted().toList();
    }

    // ---------- 기본 설정 · 설정 화면 정보 ----------

    @Test
    void 새_방은_추천_구성이고_랜덤_후보는_특수_직업_전부다() {
        RoleSetup setup = assigner.defaultSetup();

        assertThat(setup.mode()).isEqualTo(RoleSetupMode.RECOMMENDED);
        assertThat(setup.customCompositions()).isEmpty();
        assertThat(setup.randomCandidates()).isEqualTo(ALL_SPECIALS);
    }

    @Test
    void 직업은_해적_진영부터_추천_구성에_먼저_나오는_순서이고_선원이_맨_뒤다() {
        assertThat(assigner.roles()).extracting(RoleDefinition::code).containsExactly(
                "PIRATE_RAIDER", "PIRATE_PARROT",
                "CREW_CAPTAIN", "CREW_DOCTOR", "CREW_LOOKOUT", "CREW_MONKEY", "CREW_BOATSWAIN", "CREW_DRUNK",
                "CREW_SAILOR");
    }

    @Test
    void 추천_구성표는_4인부터_12인까지_인원수만큼이다() {
        List<RoleComposition> recommended = assigner.recommendedCompositions();

        assertThat(recommended).extracting(RoleComposition::playerCount)
                .containsExactlyElementsOf(IntStream.rangeClosed(4, 12).boxed().toList());
        recommended.forEach(c -> assertThat(c.roles()).hasSize(c.playerCount()));
    }

    // ---------- 추천 ----------

    @ParameterizedTest
    @ValueSource(ints = {4, 7, 9, 12})
    void 설정이_없거나_추천이면_추천_구성대로_배정한다(int playerCount) {
        List<String> recommended = RoleAssigner.RECOMMENDED.get(playerCount).stream().sorted().toList();

        assertThat(sorted(assigner.assign(playerCount, null))).isEqualTo(recommended);
        assertThat(sorted(assigner.assign(playerCount, assigner.defaultSetup()))).isEqualTo(recommended);
    }

    // ---------- 커스텀 ----------

    @Test
    void 커스텀은_편집한_인원수의_구성대로_배정한다() {
        RoleSetup setup = assigner.normalize(custom(7,
                "PIRATE_RAIDER", "PIRATE_PARROT", "CREW_CAPTAIN", "CREW_DOCTOR", "CREW_DRUNK", "CREW_SAILOR", "CREW_SAILOR"));

        assertThat(sorted(assigner.assign(7, setup))).isEqualTo(sortedCodes(
                "PIRATE_RAIDER", "PIRATE_PARROT", "CREW_CAPTAIN", "CREW_DOCTOR", "CREW_DRUNK", "CREW_SAILOR", "CREW_SAILOR"));
    }

    @Test
    void 커스텀에서_편집하지_않은_인원수는_추천_구성을_쓴다() {
        RoleSetup setup = assigner.normalize(custom(7,
                "PIRATE_RAIDER", "PIRATE_RAIDER", "CREW_CAPTAIN", "CREW_DOCTOR", "CREW_SAILOR", "CREW_SAILOR", "CREW_SAILOR"));

        assertThat(sorted(assigner.assign(5, setup)))
                .isEqualTo(RoleAssigner.RECOMMENDED.get(5).stream().sorted().toList());
    }

    @Test
    void 커스텀은_특수_직업을_여러_명_넣을_수_있다() {
        RoleSetup setup = assigner.normalize(custom(5,
                "PIRATE_RAIDER", "CREW_DOCTOR", "CREW_DOCTOR", "CREW_CAPTAIN", "CREW_CAPTAIN"));

        assertThat(sorted(assigner.assign(5, setup))).containsExactly(
                "CREW_CAPTAIN", "CREW_CAPTAIN", "CREW_DOCTOR", "CREW_DOCTOR", "PIRATE_RAIDER");
    }

    @Test
    void 직업_수가_인원수와_다르면_거부한다() {
        assertThatThrownBy(() -> assigner.normalize(custom(5, "PIRATE_RAIDER", "CREW_CAPTAIN", "CREW_DOCTOR", "CREW_SAILOR")))
                .isInstanceOf(IllegalArgumentException.class)
                .hasMessageContaining("5인")
                .hasMessageContaining("직업 수(4)");
    }

    @Test
    void 공격할_해적이_없으면_거부한다() {
        assertThatThrownBy(() -> assigner.normalize(custom(4, "PIRATE_PARROT", "CREW_CAPTAIN", "CREW_DOCTOR", "CREW_SAILOR")))
                .isInstanceOf(IllegalArgumentException.class)
                .hasMessageContaining("공격할 수 있는 해적");
    }

    @Test
    void 해적_진영이_선원_진영보다_적지_않으면_거부한다() {
        assertThatThrownBy(() -> assigner.normalize(custom(4, "PIRATE_RAIDER", "PIRATE_PARROT", "CREW_CAPTAIN", "CREW_SAILOR")))
                .isInstanceOf(IllegalArgumentException.class)
                .hasMessageContaining("해적 진영(2명)");
    }

    @Test
    void 없는_직업이나_범위_밖_인원수나_중복_인원수는_거부한다() {
        assertThatThrownBy(() -> assigner.normalize(custom(4, "PIRATE_RAIDER", "CREW_GHOST", "CREW_DOCTOR", "CREW_SAILOR")))
                .isInstanceOf(IllegalArgumentException.class)
                .hasMessageContaining("CREW_GHOST");
        assertThatThrownBy(() -> assigner.normalize(custom(13)))
                .isInstanceOf(IllegalArgumentException.class)
                .hasMessageContaining("4~12명");

        List<String> four = List.of("PIRATE_RAIDER", "CREW_CAPTAIN", "CREW_DOCTOR", "CREW_SAILOR");
        RoleSetup twice = new RoleSetup(RoleSetupMode.CUSTOM,
                List.of(new RoleComposition(4, four), new RoleComposition(4, four)), null);
        assertThatThrownBy(() -> assigner.normalize(twice))
                .isInstanceOf(IllegalArgumentException.class)
                .hasMessageContaining("두 번");
    }

    @Test
    void 모드가_없으면_거부한다() {
        assertThatThrownBy(() -> assigner.normalize(new RoleSetup(null, List.of(), null)))
                .isInstanceOf(IllegalArgumentException.class)
                .hasMessageContaining("mode");
    }

    @Test
    void 지금_모드가_아니어도_커스텀_표를_검증한다() {
        RoleSetup randomWithBadTable = new RoleSetup(RoleSetupMode.RANDOM,
                List.of(new RoleComposition(4, List.of("PIRATE_RAIDER"))), null);

        assertThatThrownBy(() -> assigner.normalize(randomWithBadTable))
                .isInstanceOf(IllegalArgumentException.class);
    }

    @Test
    void 커스텀_표는_인원수_순으로_정리한다() {
        RoleSetup setup = assigner.normalize(new RoleSetup(RoleSetupMode.CUSTOM, List.of(
                new RoleComposition(5, List.of("PIRATE_RAIDER", "CREW_CAPTAIN", "CREW_DOCTOR", "CREW_SAILOR", "CREW_SAILOR")),
                new RoleComposition(4, List.of("PIRATE_RAIDER", "CREW_CAPTAIN", "CREW_DOCTOR", "CREW_SAILOR"))), null));

        assertThat(setup.customCompositions()).extracting(RoleComposition::playerCount).containsExactly(4, 5);
    }

    @Test
    void 저장된_커스텀_구성이_지금_규칙에_맞지_않으면_게임을_시작하지_않는다() {
        // 저장할 때는 맞았지만 서버 재시작 후 직업이 빠진 경우처럼 normalize를 거치지 않은 설정
        RoleSetup stale = custom(4, "PIRATE_RAIDER", "CREW_CAPTAIN", "CREW_DOCTOR", "CREW_GHOST");

        assertThatThrownBy(() -> assigner.assign(4, stale))
                .isInstanceOf(GameRuleException.class)
                .hasMessageContaining("CREW_GHOST");
    }

    // ---------- 랜덤 ----------

    @Test
    void 랜덤_후보는_표시_순서로_정리하고_중복과_해적_선원을_뺀다() {
        RoleSetup setup = assigner.normalize(random(List.of(
                "CREW_DRUNK", "PIRATE_RAIDER", "CREW_CAPTAIN", "CREW_DRUNK", "CREW_SAILOR", "PIRATE_PARROT")));

        assertThat(setup.randomCandidates()).containsExactly("PIRATE_PARROT", "CREW_CAPTAIN", "CREW_DRUNK");
    }

    @Test
    void 랜덤_후보가_null이면_특수_직업_전부이고_비어_있으면_특수_직업이_없다() {
        assertThat(assigner.normalize(random(null)).randomCandidates()).isEqualTo(ALL_SPECIALS);
        assertThat(assigner.normalize(random(List.of())).randomCandidates()).isEmpty();
    }

    @Test
    void 랜덤_후보에_없는_직업이_있으면_거부한다() {
        assertThatThrownBy(() -> assigner.normalize(random(List.of("CREW_GHOST"))))
                .isInstanceOf(IllegalArgumentException.class)
                .hasMessageContaining("CREW_GHOST");
    }

    @ParameterizedTest
    @ValueSource(ints = {4, 5, 6, 7, 8, 9, 10, 11, 12})
    void 랜덤은_해적_진영_수가_추천과_같고_공격할_해적이_있고_특수_직업은_한_명씩이다(int playerCount) {
        long recommendedPirates = RoleAssigner.RECOMMENDED.get(playerCount).stream()
                .filter(code -> code.startsWith("PIRATE_")).count();
        RoleSetup setup = assigner.normalize(random(null));

        for (int i = 0; i < 50; i++) {
            List<RoleDefinition> roles = assigner.assign(playerCount, setup);

            assertThat(roles).hasSize(playerCount);
            assertThat(roles.stream().filter(RoleDefinition::isPirate).count()).isEqualTo(recommendedPirates);
            assertThat(roles).anyMatch(RoleDefinition::isRaider);
            List<String> specials = roles.stream().map(RoleDefinition::code)
                    .filter(ALL_SPECIALS::contains).toList();
            assertThat(specials).doesNotHaveDuplicates();
        }
    }

    @Test
    void 랜덤은_후보에_없는_특수_직업을_넣지_않고_모자라면_해적과_선원으로_채운다() {
        RoleSetup setup = assigner.normalize(random(List.of("CREW_CAPTAIN", "CREW_DOCTOR")));

        // 8인: 해적 진영 2(앵무새가 후보에 없어 해적 2) + 선원 진영 6(선장·선의 + 선원 4)
        assertThat(sorted(assigner.assign(8, setup))).isEqualTo(sortedCodes(
                "PIRATE_RAIDER", "PIRATE_RAIDER",
                "CREW_CAPTAIN", "CREW_DOCTOR", "CREW_SAILOR", "CREW_SAILOR", "CREW_SAILOR", "CREW_SAILOR"));
    }

    @Test
    void 랜덤은_선원_진영_후보가_충분하면_선원_없이_특수_직업으로_채운다() {
        RoleSetup setup = assigner.normalize(random(null));

        for (int i = 0; i < 30; i++) {
            List<String> codes = assigner.assign(7, setup).stream().map(RoleDefinition::code).toList();
            // 7인: 선원 진영 5자리, 선원 진영 특수 직업 후보 6개
            assertThat(codes).doesNotContain("CREW_SAILOR");
        }
    }

    @Test
    void 랜덤은_판마다_구성이_달라지고_앵무새도_해적도_뽑힐_수_있다() {
        RoleSetup setup = assigner.normalize(random(null));

        Set<List<String>> compositions = new HashSet<>();
        Set<Long> raiderCounts = new HashSet<>();
        for (int i = 0; i < 100; i++) {
            List<RoleDefinition> roles = assigner.assign(7, setup);
            compositions.add(sorted(roles));
            raiderCounts.add(roles.stream().filter(RoleDefinition::isRaider).count());
        }

        assertThat(compositions).hasSizeGreaterThan(1);
        assertThat(raiderCounts).containsExactlyInAnyOrder(1L, 2L); // 해적 + {해적 | 앵무새}
    }
}
