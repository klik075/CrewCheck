package com.WhoisntCitizen_server.member.controller;

import com.WhoisntCitizen_server.member.dto.OAuthUserInfo;
import com.WhoisntCitizen_server.member.entity.AuthProvider;
import com.WhoisntCitizen_server.member.service.GoogleAuthService;
import com.WhoisntCitizen_server.member.service.KakaoAuthService;
import com.WhoisntCitizen_server.user.repository.UserRepository;
import org.junit.jupiter.api.Test;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.boot.test.context.SpringBootTest;
import org.springframework.boot.webmvc.test.autoconfigure.AutoConfigureMockMvc;
import org.springframework.http.MediaType;
import org.springframework.test.context.bean.override.mockito.MockitoBean;
import org.springframework.test.web.servlet.MockMvc;

import static org.assertj.core.api.Assertions.assertThat;
import static org.mockito.Mockito.when;
import static org.springframework.test.web.servlet.request.MockMvcRequestBuilders.post;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.jsonPath;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.status;

/** OAuth(구글/카카오) 로그인: 토큰 없이 접근 가능하고, 최초 로그인은 가입, 재로그인은 같은 계정으로 처리된다. */
@SpringBootTest
@AutoConfigureMockMvc
class MemberOAuthLoginTest {

    @Autowired MockMvc mvc;
    @Autowired UserRepository userRepository;
    @MockitoBean GoogleAuthService google;
    @MockitoBean KakaoAuthService kakao;

    private static final String BODY = "{\"token\":\"any-token\"}";

    private static OAuthUserInfo info(AuthProvider p, String id, String nickname) {
        return OAuthUserInfo.builder().provider(p).providerId(id).email("a@b.c").nickname(nickname).build();
    }

    @Test
    void 구글_최초_로그인은_가입되고_JWT가_발급된다() throws Exception {
        when(google.verifyToken("any-token")).thenReturn(info(AuthProvider.GOOGLE, "g-1", "구글유저"));

        mvc.perform(post("/api/members/login/google").contentType(MediaType.APPLICATION_JSON).content(BODY))
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.username").value("google_g-1"))
                .andExpect(jsonPath("$.nickname").value("구글유저"))
                .andExpect(jsonPath("$.accessToken").isNotEmpty());
    }

    @Test
    void 같은_계정으로_다시_로그인하면_같은_회원이다() throws Exception {
        when(kakao.verifyToken("any-token")).thenReturn(info(AuthProvider.KAKAO, "k-1", "카카오유저"));

        String first = mvc.perform(post("/api/members/login/kakao").contentType(MediaType.APPLICATION_JSON).content(BODY))
                .andExpect(status().isOk()).andReturn().getResponse().getContentAsString();
        long users = userRepository.count();
        String second = mvc.perform(post("/api/members/login/kakao").contentType(MediaType.APPLICATION_JSON).content(BODY))
                .andExpect(status().isOk()).andReturn().getResponse().getContentAsString();

        assertThat(userRepository.count()).isEqualTo(users);
        assertThat(userIdOf(second)).isEqualTo(userIdOf(first));
    }

    private static String userIdOf(String json) {
        int i = json.indexOf("\"userId\":") + 9;
        return json.substring(i, json.indexOf(',', i));
    }

    @Test
    void 닉네임이_겹치거나_길어도_가입된다() throws Exception {
        String longName = "아주아주아주긴닉네임을가진사용자입니다만괜찮을까요";
        when(google.verifyToken("any-token")).thenReturn(info(AuthProvider.GOOGLE, "g-2", longName));
        when(kakao.verifyToken("any-token")).thenReturn(info(AuthProvider.KAKAO, "k-2", longName));

        mvc.perform(post("/api/members/login/google").contentType(MediaType.APPLICATION_JSON).content(BODY))
                .andExpect(status().isOk());
        mvc.perform(post("/api/members/login/kakao").contentType(MediaType.APPLICATION_JSON).content(BODY))
                .andExpect(status().isOk());
    }

    @Test
    void 검증에_실패하면_토큰이_발급되지_않는다() throws Exception {
        when(google.verifyToken("any-token")).thenThrow(new IllegalArgumentException("유효하지 않거나 위조된 구글 ID 토큰입니다."));

        mvc.perform(post("/api/members/login/google").contentType(MediaType.APPLICATION_JSON).content(BODY))
                .andExpect(status().is4xxClientError());
    }

    @Test
    void 토큰이_비어_있으면_400() throws Exception {
        mvc.perform(post("/api/members/login/google").contentType(MediaType.APPLICATION_JSON).content("{\"token\":\"\"}"))
                .andExpect(status().isBadRequest());
    }
}
