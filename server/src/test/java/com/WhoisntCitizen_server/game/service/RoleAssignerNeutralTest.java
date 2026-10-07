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

import java.util.ArrayList;
import java.util.List;
import java.util.Random;

import static org.assertj.core.api.Assertions.assertThat;

/** V6 신규 직업과 직업 배정: 요리사는 랜덤 후보가 되고, 제3 세력은 커스텀 구성에서만 고를 수 있다. */
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

    @Test
    void 요리사는_랜덤_후보가_되고_제3_세력은_랜덤_후보가_아니다() {
        assertThat(assigner.specialRoleCodes()).contains("PIRATE_COOK").doesNotContainAnyElementsOf(NEUTRALS);
        assertThat(assigner.roles()).extracting(RoleDefinition::code).containsAll(NEUTRALS); // 커스텀 편집용으로는 보인다
    }

    @Test
    void 랜덤_후보로_제3_세력을_보내도_빠지고_배정되지_않는다() {
        RoleSetup setup = assigner.normalize(new RoleSetup(RoleSetupMode.RANDOM, List.of(),
                List.of("PIRATE_COOK", "NEUTRAL_KRAKEN", "NEUTRAL_MERMAID", "CREW_CAPTAIN")));

        assertThat(setup.randomCandidates()).containsExactly("PIRATE_COOK", "CREW_CAPTAIN");
        for (int i = 0; i < 50; i++) {
            assertThat(assigner.assign(9, setup)).noneMatch(RoleDefinition::isNeutral);
        }
    }

    @Test
    void 커스텀_구성에는_제3_세력을_넣을_수_있다() {
        List<String> codes = List.of("PIRATE_RAIDER", "NEUTRAL_KRAKEN", "NEUTRAL_MERMAID",
                "CREW_CAPTAIN", "CREW_DOCTOR", "CREW_SAILOR");
        RoleSetup setup = assigner.normalize(
                new RoleSetup(RoleSetupMode.CUSTOM, List.of(new RoleComposition(6, codes)), null));

        assertThat(assigner.assign(6, setup)).extracting(RoleDefinition::code)
                .containsExactlyInAnyOrderElementsOf(codes);
    }
}
