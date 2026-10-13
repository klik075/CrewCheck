package com.WhoisntCitizen_server.game.controller;

import com.WhoisntCitizen_server.support.TestRoles;
import org.junit.jupiter.api.Test;
import org.springframework.test.web.servlet.MockMvc;
import org.springframework.test.web.servlet.setup.MockMvcBuilders;

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
}
