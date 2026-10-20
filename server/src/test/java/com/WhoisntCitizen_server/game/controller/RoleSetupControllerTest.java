package com.WhoisntCitizen_server.game.controller;

import com.WhoisntCitizen_server.game.service.RoleAssigner;
import com.WhoisntCitizen_server.jobs.domain.ActionCode;
import com.WhoisntCitizen_server.jobs.domain.Faction;
import com.WhoisntCitizen_server.jobs.domain.RoleDefinition;
import com.WhoisntCitizen_server.jobs.service.RoleCatalog;
import com.WhoisntCitizen_server.support.TestRoles;
import org.junit.jupiter.api.Test;
import org.springframework.test.web.servlet.MockMvc;
import org.springframework.test.web.servlet.setup.MockMvcBuilders;

import java.util.ArrayList;
import java.util.List;
import java.util.Random;

import static org.hamcrest.Matchers.hasSize;
import static org.hamcrest.Matchers.not;
import static org.hamcrest.Matchers.hasItem;
import static org.springframework.test.web.servlet.request.MockMvcRequestBuilders.get;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.jsonPath;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.status;

/**
 * 직업 설정 화면 선택지 API (GET /api/v1/role-setup/options).
 * Unity JsonUtility가 읽을 수 있도록 Map 없이 객체 배열이고 진영은 문자열이다.
 */
class RoleSetupControllerTest {

    private final MockMvc mvc = MockMvcBuilders
            .standaloneSetup(new RoleSetupController(TestRoles.assigner(7)))
            .build();

    @Test
    void 인원_범위와_직업_목록과_랜덤_후보와_추천_구성을_돌려준다() throws Exception {
        mvc.perform(get("/api/v1/role-setup/options"))
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.minPlayers").value(4))
                .andExpect(jsonPath("$.maxPlayers").value(12))
                .andExpect(jsonPath("$.roles", hasSize(9)))
                .andExpect(jsonPath("$.roles[0].code").value("PIRATE_RAIDER"))
                .andExpect(jsonPath("$.roles[0].name").value("해적"))
                .andExpect(jsonPath("$.roles[0].faction").value("PIRATE"))
                .andExpect(jsonPath("$.roles[8].code").value("CREW_SAILOR"))
                .andExpect(jsonPath("$.randomCandidates", hasSize(7)))
                .andExpect(jsonPath("$.randomCandidates", not(hasItem("PIRATE_RAIDER"))))
                .andExpect(jsonPath("$.randomCandidates", not(hasItem("CREW_SAILOR"))))
                .andExpect(jsonPath("$.recommended", hasSize(9)))
                .andExpect(jsonPath("$.recommended[0].playerCount").value(4))
                .andExpect(jsonPath("$.recommended[0].roles", hasSize(4)));
    }

    @Test
    void 제3_세력도_랜덤_후보로_돌려주고_선원_진영_특수_직업_뒤에_둔다() throws Exception {
        List<RoleDefinition> roles = new ArrayList<>(TestRoles.ALL);
        roles.add(new RoleDefinition("NEUTRAL_KRAKEN", "크라켄", Faction.NEUTRAL, ActionCode.KRAKEN_MARK));
        roles.add(new RoleDefinition("NEUTRAL_MERMAID", "인어", Faction.NEUTRAL, null));
        MockMvc withNeutrals = MockMvcBuilders
                .standaloneSetup(new RoleSetupController(new RoleAssigner(new Random(7), new RoleCatalog(roles))))
                .build();

        withNeutrals.perform(get("/api/v1/role-setup/options"))
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.roles", hasSize(11)))
                .andExpect(jsonPath("$.roles[8].code").value("NEUTRAL_KRAKEN"))
                .andExpect(jsonPath("$.roles[8].faction").value("NEUTRAL"))
                .andExpect(jsonPath("$.roles[10].code").value("CREW_SAILOR"))
                .andExpect(jsonPath("$.randomCandidates", hasSize(9)))
                .andExpect(jsonPath("$.randomCandidates[6]").value("CREW_DRUNK"))
                .andExpect(jsonPath("$.randomCandidates[7]").value("NEUTRAL_KRAKEN"))
                .andExpect(jsonPath("$.randomCandidates[8]").value("NEUTRAL_MERMAID"));
    }
}
