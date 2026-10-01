package com.WhoisntCitizen_server.game.service;

import com.WhoisntCitizen_server.common.exception.GameRuleException;
import com.WhoisntCitizen_server.jobs.domain.ActionCode;
import com.WhoisntCitizen_server.jobs.domain.Faction;
import com.WhoisntCitizen_server.jobs.domain.RoleDefinition;
import com.WhoisntCitizen_server.jobs.service.RoleCatalog;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.params.ParameterizedTest;
import org.junit.jupiter.params.provider.ValueSource;

import java.util.HashMap;
import java.util.List;
import java.util.Map;
import java.util.Random;
import java.util.stream.IntStream;

import static org.assertj.core.api.Assertions.assertThat;
import static org.assertj.core.api.Assertions.assertThatThrownBy;
import static org.mockito.ArgumentMatchers.anyString;
import static org.mockito.Mockito.mock;
import static org.mockito.Mockito.when;

/** 6단계: 인원수별 직업 구성표. DB 대신 RoleCatalog를 Mockito로 대신한다. */
class RoleAssignerCompositionTest {

    private static final Map<String, RoleDefinition> ROLES = Map.of(
            "PIRATE_RAIDER", new RoleDefinition("PIRATE_RAIDER", "해적", Faction.PIRATE, ActionCode.SELECT_ATTACK_TARGET),
            "PIRATE_PARROT", new RoleDefinition("PIRATE_PARROT", "앵무새", Faction.PIRATE, ActionCode.WATCH_ACTION),
            "CREW_CAPTAIN", new RoleDefinition("CREW_CAPTAIN", "선장", Faction.CREW, ActionCode.INVESTIGATE_FACTION),
            "CREW_DOCTOR", new RoleDefinition("CREW_DOCTOR", "선의", Faction.CREW, ActionCode.PROTECT),
            "CREW_LOOKOUT", new RoleDefinition("CREW_LOOKOUT", "망루지기", Faction.CREW, ActionCode.WATCH_VISITORS),
            "CREW_BOATSWAIN", new RoleDefinition("CREW_BOATSWAIN", "갑판장", Faction.CREW, ActionCode.BLOCK),
            "CREW_DRUNK", new RoleDefinition("CREW_DRUNK", "주정뱅이", Faction.CREW, ActionCode.READ_CORPSE_ROLE),
            "CREW_MONKEY", new RoleDefinition("CREW_MONKEY", "원숭이", Faction.CREW, null),
            "CREW_SAILOR", new RoleDefinition("CREW_SAILOR", "선원", Faction.CREW, null));

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

    private final RoleAssigner assigner = new RoleAssigner(new Random(7), catalog(ROLES));

    private List<String> sortedCodes(int playerCount) {
        return assigner.assign(playerCount).stream().map(RoleDefinition::code).sorted().toList();
    }

    @ParameterizedTest
    @ValueSource(ints = {4, 5, 6, 7, 8, 9, 10, 11, 12})
    void 인원수만큼_배정되고_해적_진영은_3분의_1이며_공격할_해적이_있다(int playerCount) {
        List<RoleDefinition> roles = assigner.assign(playerCount);

        assertThat(roles).hasSize(playerCount);
        assertThat(roles.stream().filter(RoleDefinition::isPirate).count()).isEqualTo(Math.max(1, playerCount / 3));
        assertThat(roles).anyMatch(RoleDefinition::isRaider);
    }

    @Test
    void 사인과_육인은_기존_구성과_같아_Postman_시나리오가_그대로_동작한다() {
        assertThat(sortedCodes(4)).containsExactly(
                "CREW_CAPTAIN", "CREW_DOCTOR", "CREW_SAILOR", "PIRATE_RAIDER");
        assertThat(sortedCodes(6)).containsExactly(
                "CREW_CAPTAIN", "CREW_DOCTOR", "CREW_SAILOR", "CREW_SAILOR", "PIRATE_RAIDER", "PIRATE_RAIDER");
    }

    @Test
    void 구인부터_모든_특수_직업이_나온다() {
        assertThat(sortedCodes(9)).containsExactly(
                "CREW_BOATSWAIN", "CREW_CAPTAIN", "CREW_DOCTOR", "CREW_DRUNK", "CREW_LOOKOUT", "CREW_MONKEY",
                "PIRATE_PARROT", "PIRATE_RAIDER", "PIRATE_RAIDER");
    }

    @Test
    void 배정_순서는_섞인다() {
        List<List<String>> orders = IntStream.range(0, 20)
                .mapToObj(i -> assigner.assign(9).stream().map(RoleDefinition::code).toList())
                .distinct()
                .toList();

        assertThat(orders).hasSizeGreaterThan(1);
    }

    @ParameterizedTest
    @ValueSource(ints = {3, 13})
    void 인원_범위를_벗어나면_거부한다(int playerCount) {
        assertThatThrownBy(() -> assigner.assign(playerCount))
                .isInstanceOf(GameRuleException.class)
                .hasMessageContaining("4~12명");
    }

    @Test
    void 구성표의_직업이_DB에_없으면_생성할_때_실패한다() {
        Map<String, RoleDefinition> withoutParrot = new HashMap<>(ROLES);
        withoutParrot.remove("PIRATE_PARROT");

        assertThatThrownBy(() -> new RoleAssigner(new Random(7), catalog(withoutParrot)))
                .isInstanceOf(IllegalStateException.class)
                .hasMessageContaining("PIRATE_PARROT");
    }
}