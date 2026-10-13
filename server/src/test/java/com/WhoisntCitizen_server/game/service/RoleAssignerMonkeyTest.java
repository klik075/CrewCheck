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
import java.util.HashSet;
import java.util.List;
import java.util.Random;
import java.util.Set;

import static com.WhoisntCitizen_server.support.TestRoles.BOATSWAIN;
import static com.WhoisntCitizen_server.support.TestRoles.CAPTAIN;
import static com.WhoisntCitizen_server.support.TestRoles.DOCTOR;
import static com.WhoisntCitizen_server.support.TestRoles.DRUNK;
import static com.WhoisntCitizen_server.support.TestRoles.LOOKOUT;
import static com.WhoisntCitizen_server.support.TestRoles.MONKEY;
import static com.WhoisntCitizen_server.support.TestRoles.PARROT;
import static com.WhoisntCitizen_server.support.TestRoles.RAIDER;
import static com.WhoisntCitizen_server.support.TestRoles.SAILOR;
import static org.assertj.core.api.Assertions.assertThat;
import static org.assertj.core.api.Assertions.assertThatThrownBy;

/**
 * 5단계: 원숭이의 위장 직업 배정. 위장 직업은 이번 게임 구성에 있는 직업 중에서만 고른다.
 * 위장 후보: 원숭이를 뺀 선원 진영 전부 + 능력이 없는 제3 세력(유령 선장·인어).
 */
class RoleAssignerMonkeyTest {

    private static final RoleDefinition COOK =
            new RoleDefinition("PIRATE_COOK", "요리사", Faction.PIRATE, ActionCode.BAN_VOTE);
    private static final RoleDefinition SIREN =
            new RoleDefinition("NEUTRAL_SIREN", "세이렌", Faction.NEUTRAL, ActionCode.SEDUCE);
    private static final RoleDefinition KRAKEN =
            new RoleDefinition("NEUTRAL_KRAKEN", "크라켄", Faction.NEUTRAL, ActionCode.KRAKEN_MARK);
    private static final RoleDefinition GHOST_CAPTAIN =
            new RoleDefinition("NEUTRAL_GHOST_CAPTAIN", "유령 선장", Faction.NEUTRAL, null);
    private static final RoleDefinition MERMAID =
            new RoleDefinition("NEUTRAL_MERMAID", "인어", Faction.NEUTRAL, null);

    private final RoleAssigner assigner = new RoleAssigner(new Random(7), catalog());

    private static RoleCatalog catalog() {
        List<RoleDefinition> roles = new ArrayList<>(TestRoles.ALL);
        roles.addAll(List.of(COOK, SIREN, KRAKEN, GHOST_CAPTAIN, MERMAID));
        return new RoleCatalog(roles);
    }

    /** 이 구성에서 원숭이의 위장 직업을 여러 번 뽑았을 때 나온 직업들 */
    private Set<RoleDefinition> disguisesIn(List<RoleDefinition> composition) {
        Set<RoleDefinition> seen = new HashSet<>();
        for (int i = 0; i < 300; i++) {
            seen.add(assigner.shownRoleOf(MONKEY, composition));
        }
        return seen;
    }

    private static RoleSetup custom(int playerCount, String... codes) {
        return new RoleSetup(RoleSetupMode.CUSTOM, List.of(new RoleComposition(playerCount, List.of(codes))), null);
    }

    @Test
    void 원숭이가_아니면_실제_직업이_그대로_보인다() {
        assertThat(assigner.shownRoleOf(CAPTAIN, List.of(RAIDER, CAPTAIN, MONKEY, SAILOR))).isSameAs(CAPTAIN);
    }

    // ---------- 추천 구성 ----------

    @Test
    void 칠인_추천_구성이면_갑판장과_주정뱅이로는_보이지_않는다() {
        // 해적 2, 선장, 선의, 망루지기, 원숭이, 선원
        assertThat(disguisesIn(assigner.assign(7))).containsExactlyInAnyOrder(CAPTAIN, DOCTOR, LOOKOUT, SAILOR);
    }

    @Test
    void 팔인_추천_구성이면_갑판장까지_보이고_주정뱅이로는_보이지_않는다() {
        assertThat(disguisesIn(assigner.assign(8)))
                .containsExactlyInAnyOrder(CAPTAIN, DOCTOR, LOOKOUT, BOATSWAIN, SAILOR);
    }

    @Test
    void 구인_추천_구성이면_선원이_없으므로_특수_선원_다섯_직업으로_보인다() {
        assertThat(disguisesIn(assigner.assign(9)))
                .containsExactlyInAnyOrder(CAPTAIN, DOCTOR, LOOKOUT, BOATSWAIN, DRUNK);
    }

    // ---------- 위장 후보 ----------

    @Test
    void 능력이_없는_제3_세력인_유령_선장과_인어로도_보인다() {
        assertThat(disguisesIn(List.of(RAIDER, MONKEY, GHOST_CAPTAIN, MERMAID, KRAKEN)))
                .containsExactlyInAnyOrder(GHOST_CAPTAIN, MERMAID);
    }

    @Test
    void 해적_진영과_능력이_있는_제3_세력으로는_보이지_않는다() {
        assertThat(disguisesIn(List.of(RAIDER, PARROT, COOK, SIREN, KRAKEN, MONKEY, CAPTAIN)))
                .containsExactly(CAPTAIN);
    }

    @Test
    void 원숭이가_둘이어도_서로의_원숭이로는_보이지_않는다() {
        assertThat(disguisesIn(List.of(RAIDER, MONKEY, MONKEY, DOCTOR))).containsExactly(DOCTOR);
    }

    @Test
    void 위장할_직업이_없는_구성이면_정하지_않고_예외를_던진다() {
        assertThatThrownBy(() -> assigner.shownRoleOf(MONKEY, List.of(RAIDER, MONKEY, SIREN, KRAKEN)))
                .isInstanceOf(IllegalStateException.class);
    }

    // ---------- 구성 규칙: 원숭이가 있으면 위장할 직업도 1명 이상 ----------

    @Test
    void 커스텀_구성에_원숭이가_있는데_위장할_직업이_없으면_거부한다() {
        RoleSetup setup = custom(4, "PIRATE_RAIDER", "CREW_MONKEY", "NEUTRAL_SIREN", "NEUTRAL_KRAKEN");

        assertThatThrownBy(() -> assigner.normalize(setup))
                .isInstanceOf(IllegalArgumentException.class)
                .hasMessageContaining("원숭이가 있으면");
    }

    @Test
    void 커스텀_구성에_위장할_직업이_유령_선장뿐이면_유령_선장으로_보인다() {
        RoleSetup setup = assigner.normalize(
                custom(4, "PIRATE_RAIDER", "CREW_MONKEY", "NEUTRAL_GHOST_CAPTAIN", "NEUTRAL_KRAKEN"));

        List<RoleDefinition> roles = assigner.assign(4, setup);

        assertThat(assigner.shownRoleOf(MONKEY, roles)).isSameAs(GHOST_CAPTAIN);
    }

    @Test
    void 랜덤_구성은_선원_진영_자리가_둘_이상이라_원숭이가_항상_구성_안의_직업으로_보인다() {
        // 선원 진영 후보가 원숭이뿐이면 원숭이는 매번 뽑히고 나머지 선원 진영 자리는 선원으로 채워진다.
        RoleSetup onlyMonkey = assigner.normalize(new RoleSetup(RoleSetupMode.RANDOM, List.of(), List.of("CREW_MONKEY")));

        for (int playerCount = RoleAssigner.MIN_PLAYERS; playerCount <= RoleAssigner.MAX_PLAYERS; playerCount++) {
            List<RoleDefinition> roles = assigner.assign(playerCount, onlyMonkey);

            assertThat(roles).contains(MONKEY);
            assertThat(assigner.shownRoleOf(MONKEY, roles)).isSameAs(SAILOR);
        }
    }
}
