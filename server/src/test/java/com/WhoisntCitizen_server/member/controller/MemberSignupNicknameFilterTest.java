package com.WhoisntCitizen_server.member.controller;

import com.WhoisntCitizen_server.user.repository.UserRepository;
import org.junit.jupiter.api.Test;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.boot.test.context.SpringBootTest;
import org.springframework.boot.webmvc.test.autoconfigure.AutoConfigureMockMvc;
import org.springframework.http.MediaType;
import org.springframework.test.web.servlet.MockMvc;

import static org.assertj.core.api.Assertions.assertThat;
import static org.springframework.test.web.servlet.request.MockMvcRequestBuilders.post;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.jsonPath;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.status;

/** 회원가입: 닉네임에 욕설·금칙어가 있으면 400 NICKNAME_NOT_ALLOWED로 거절하고 가입되지 않는다. */
@SpringBootTest
@AutoConfigureMockMvc
class MemberSignupNicknameFilterTest {

    @Autowired MockMvc mvc;
    @Autowired UserRepository userRepository;

    private static String body(String username, String nickname) {
        return "{\"username\":\"" + username + "\",\"password\":\"Passw0rd!x\",\"nickname\":\"" + nickname + "\"}";
    }

    @Test
    void 금칙어_닉네임은_400과_NICKNAME_NOT_ALLOWED로_거절된다() throws Exception {
        mvc.perform(post("/api/members/signup").contentType(MediaType.APPLICATION_JSON).content(body("badnick1", "시발")))
                .andExpect(status().isBadRequest())
                .andExpect(jsonPath("$.code").value("NICKNAME_NOT_ALLOWED"))
                .andExpect(jsonPath("$.message").isNotEmpty());

        assertThat(userRepository.existsByNickname("시발")).isFalse();
    }

    @Test
    void 정상_닉네임은_가입된다() throws Exception {
        mvc.perform(post("/api/members/signup").contentType(MediaType.APPLICATION_JSON).content(body("goodnick1", "착한이")))
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.nickname").value("착한이"));
    }
}
