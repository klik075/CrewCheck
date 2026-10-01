package com.WhoisntCitizen_server.game.service;

import com.WhoisntCitizen_server.jobs.domain.ActionCode;
import com.WhoisntCitizen_server.jobs.domain.Faction;
import com.WhoisntCitizen_server.jobs.domain.RoleDefinition;
import com.WhoisntCitizen_server.jobs.service.RoleCatalog;
import org.junit.jupiter.api.Test;

import java.util.HashMap;
import java.util.HashSet;
import java.util.Map;
import java.util.Random;
import java.util.Set;

import static org.assertj.core.api.Assertions.assertThat;
import static org.assertj.core.api.Assertions.assertThatThrownBy;
import static org.mockito.ArgumentMatchers.anyString;
import static org.mockito.Mockito.mock;
import static org.mockito.Mockito.when;

/** 5단계: 원숭이의 위장 직업 배정. DB 대신 RoleCatalog를 Mockito로 대신한다. */
class RoleAssignerMonkeyTest {

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
    // 6단계부터 RoleAssigner가 생성 시 구성표의 직업도 조회하므로 전체 직업이 필요하다.
    private static final RoleDefinition RAIDER =
            new RoleDefinition("PIRATE_RAIDER", "해적", Faction.PIRATE, ActionCode.SELECT_ATTACK_TARGET);
    private static final RoleDefinition PARROT =
            new RoleDefinition("PIRATE_PARROT", "앵무새", Faction.PIRATE, ActionCode.WATCH_ACTION);
    private static final RoleDefinition SAILOR =
            new RoleDefinition("CREW_SAILOR", "선원", Faction.CREW, null);

    private static final Map<String, RoleDefinition> ROLES = Map.of(
            "CREW_CAPTAIN", CAPTAIN, "CREW_DOCTOR", DOCTOR, "CREW_LOOKOUT", LOOKOUT,
            "CREW_BOATSWAIN", BOATSWAIN, "CREW_DRUNK", DRUNK, "CREW_MONKEY", MONKEY,
            "PIRATE_RAIDER", RAIDER, "PIRATE_PARROT", PARROT, "CREW_SAILOR", SAILOR);

    private static RoleCatalog catalog(Map<String, RoleDefinition> roles) {
        RoleCatalog catalog = mock(RoleCatalog.class);
        when(catalog.get(anyString())).thenAnswer(inv -> {
            RoleDefinition role = roles.get(inv.<String>getArgument(0));
            if (role == null) {
                throw new IllegalStateException("없는 직업: " + inv.getArgument(0));
            }
            return role;
        });
        return catalog;
    }

    @Test
    void 원숭이가_아니면_실제_직업이_그대로_보인다() {
        RoleAssigner assigner = new RoleAssigner(new Random(7), catalog(ROLES));

        assertThat(assigner.shownRoleOf(CAPTAIN)).isSameAs(CAPTAIN);
    }

    @Test
    void 원숭이는_위장_후보_5개_중_하나로_보이고_모든_후보가_나올_수_있다() {
        RoleAssigner assigner = new RoleAssigner(new Random(7), catalog(ROLES));

        Set<RoleDefinition> seen = new HashSet<>();
        for (int i = 0; i < 200; i++) {
            seen.add(assigner.shownRoleOf(MONKEY));
        }

        assertThat(seen).containsExactlyInAnyOrder(CAPTAIN, DOCTOR, LOOKOUT, BOATSWAIN, DRUNK);
    }

    @Test
    void 위장_후보_직업이_DB에_없으면_생성할_때_실패한다() {
        Map<String, RoleDefinition> withoutDrunk = new HashMap<>(ROLES);
        withoutDrunk.remove("CREW_DRUNK");

        assertThatThrownBy(() -> new RoleAssigner(new Random(7), catalog(withoutDrunk)))
                .isInstanceOf(IllegalStateException.class)
                .hasMessageContaining("CREW_DRUNK");
    }
}