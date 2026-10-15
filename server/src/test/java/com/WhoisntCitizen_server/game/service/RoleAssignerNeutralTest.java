package com.WhoisntCitizen_server.game.service;

import com.WhoisntCitizen_server.game.dto.RoleComposition;
import com.WhoisntCitizen_server.game.dto.RoleSetup;
import com.WhoisntCitizen_server.game.dto.RoleSetupMode;
import com.WhoisntCitizen_server.jobs.domain.ActionCode;
import com.WhoisntCitizen_server.jobs.domain.Faction;
import com.WhoisntCitizen_server.jobs.domain.RoleDefinition;
import com.WhoisntCitizen_server.jobs.service.RoleCatalog;
import com.WhoisntCitizen_server.support.TestRoles;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.params.ParameterizedTest;
import org.junit.jupiter.params.provider.ValueSource;

import java.util.ArrayList;
import java.util.HashSet;
import java.util.List;
import java.util.Random;
import java.util.Set;
import java.util.function.Predicate;

import static org.assertj.core.api.Assertions.assertThat;

/**
 * V6 신규 직업과 직업 배정: 요리사와 제3 세력 모두 랜덤 후보가 된다.
 * 제3 세력은 해적 진영이 아니므로 선원 진영 자리에 섞여 나오고, 6인 이상에서 한 판에 1명까지만 나온다.
 */
class RoleAssignerNeutralTest {

    private static final List<RoleDefinition> NEW_ROLES = List.of(
            new RoleDefinition("PIRATE_COOK", "요리사", Faction.PIRATE, ActionCode.BAN_VOTE),
            new RoleDefinition("NEUTRAL_SIREN", "세이렌", Faction.NEUTRAL, ActionCode.SEDUCE),
            new RoleDefinition("NEUTRAL_KRAKEN", "크라켄", Faction.NEUTRAL, ActionCode.KRAKEN_MARK),
            new RoleDefinition("NEUTRAL_GHOST_CAPTAIN", "유령 선장", Faction.NEUTRAL, null),
            new RoleDefinition("NEUTRAL_MERMAID", "인어", Faction.NEUTRAL, null));

    private static final List<String> NEUTRALS =
            List.of("NEUTRAL_SIREN", "NEUTRAL_KRAKEN", "NEUTRAL_GHOST_CAPTAIN", "NEUTRAL_MERMAID");

    private final RoleAssigner assigner = new RoleAssigner(new Random(7), catalog());

    private static RoleCatalog catalog() {
        List<RoleDefinition> roles = new ArrayList<>(TestRoles.ALL);
        roles.addAll(NEW_ROLES);
        return new RoleCatalog(roles);
    }

    private static RoleSetup random(List<String> candidates) {
        return new RoleSetup(RoleSetupMode.RANDOM, List.of(), candidates);
    }

    private static long recommendedPirates(int playerCount) {
        return RoleAssigner.RECOMMENDED.get(playerCount).stream().filter(code -> code.startsWith("PIRATE_")).count();
    }

    private static long count(List<RoleDefinition> roles, Predicate<RoleDefinition> filter) {
        return roles.stream().filter(filter).count();
    }

    // ---------- 랜덤 후보 ----------

    @Test
    void 요리사와_제3_세력_모두_랜덤_후보이고_새_방에도_체크되어_있다() {
        assertThat(assigner.specialRoleCodes()).contains("PIRATE_COOK").containsAll(NEUTRALS);
        assertThat(assigner.defaultSetup().randomCandidates()).isEqualTo(assigner.specialRoleCodes());
        assertThat(assigner.normalize(random(null)).randomCandidates()).isEqualTo(assigner.specialRoleCodes());
    }

    @Test
    void 랜덤_후보로_보낸_제3_세력은_남고_선원_진영_특수_직업_뒤에_정렬된다() {
        RoleSetup setup = assigner.normalize(random(
                List.of("NEUTRAL_MERMAID", "PIRATE_COOK", "NEUTRAL_KRAKEN", "CREW_CAPTAIN")));

        assertThat(setup.randomCandidates())
                .containsExactly("PIRATE_COOK", "CREW_CAPTAIN", "NEUTRAL_KRAKEN", "NEUTRAL_MERMAID");
    }

    // ---------- 랜덤 배정: 선원 진영 자리에 1명까지, 6인 이상 ----------

    @ParameterizedTest
    @ValueSource(ints = {6, 7, 8, 9, 10, 11, 12})
    void 후보가_제3_세력뿐이면_선원_진영_자리에_한_명만_나오고_나머지는_선원이다(int playerCount) {
        RoleSetup setup = assigner.normalize(random(NEUTRALS));
        long pirates = recommendedPirates(playerCount);

        for (int i = 0; i < 30; i++) {
            List<RoleDefinition> roles = assigner.assign(playerCount, setup);

            assertThat(roles).hasSize(playerCount);
            assertThat(count(roles, RoleDefinition::isPirate)).isEqualTo(pirates); // 해적 진영 자리는 건드리지 않는다
            assertThat(count(roles, RoleDefinition::isNeutral)).isEqualTo(RoleAssigner.MAX_RANDOM_NEUTRALS);
            assertThat(count(roles, role -> role.code().equals("CREW_SAILOR")))
                    .isEqualTo(playerCount - pirates - RoleAssigner.MAX_RANDOM_NEUTRALS);
        }
    }

    @ParameterizedTest
    @ValueSource(ints = {4, 5})
    void 육인_미만이면_제3_세력을_후보로_골라도_나오지_않는다(int playerCount) {
        RoleSetup all = assigner.normalize(random(null));
        RoleSetup onlyNeutrals = assigner.normalize(random(NEUTRALS));

        for (int i = 0; i < 50; i++) {
            assertThat(assigner.assign(playerCount, all)).noneMatch(RoleDefinition::isNeutral);
            assertThat(assigner.assign(playerCount, onlyNeutrals)).noneMatch(RoleDefinition::isNeutral);
        }
    }

    @Test
    void 육인부터는_모든_후보로_뽑으면_제3_세력_네_직업이_모두_나올_수_있다() {
        RoleSetup setup = assigner.normalize(random(null));

        Set<String> seen = new HashSet<>();
        for (int i = 0; i < 200; i++) {
            List<RoleDefinition> roles = assigner.assign(RoleAssigner.MIN_PLAYERS_FOR_RANDOM_NEUTRAL, setup);
            roles.stream().filter(RoleDefinition::isNeutral).map(RoleDefinition::code).forEach(seen::add);
        }

        assertThat(seen).containsExactlyInAnyOrderElementsOf(NEUTRALS);
    }

    @ParameterizedTest
    @ValueSource(ints = {4, 5, 6, 7, 8, 9, 10, 11, 12})
    void 모든_후보로_뽑아도_해적_진영_수는_추천과_같고_제3_세력은_상한을_넘지_않는다(int playerCount) {
        RoleSetup setup = assigner.normalize(random(null));
        long maxNeutrals = playerCount >= RoleAssigner.MIN_PLAYERS_FOR_RANDOM_NEUTRAL ? RoleAssigner.MAX_RANDOM_NEUTRALS : 0;

        for (int i = 0; i < 100; i++) {
            List<RoleDefinition> roles = assigner.assign(playerCount, setup);

            assertThat(roles).hasSize(playerCount);
            assertThat(count(roles, RoleDefinition::isPirate)).isEqualTo(recommendedPirates(playerCount));
            assertThat(roles).anyMatch(RoleDefinition::isRaider);
            assertThat(count(roles, RoleDefinition::isNeutral)).isLessThanOrEqualTo(maxNeutrals);
        }
    }

    // ---------- 커스텀 ----------

    @Test
    void 커스텀_구성에는_랜덤의_상한과_인원_조건_없이_제3_세력을_넣을_수_있다() {
        List<String> codes = List.of("PIRATE_RAIDER", "NEUTRAL_KRAKEN", "NEUTRAL_MERMAID", "CREW_CAPTAIN", "CREW_DOCTOR");
        RoleSetup setup = assigner.normalize(
                new RoleSetup(RoleSetupMode.CUSTOM, List.of(new RoleComposition(5, codes)), null));

        assertThat(assigner.assign(5, setup)).extracting(RoleDefinition::code)
                .containsExactlyInAnyOrderElementsOf(codes);
    }
}
