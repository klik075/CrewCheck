package com.WhoisntCitizen_server.member.controller;

import com.WhoisntCitizen_server.member.dto.OAuthUserInfo;
import com.WhoisntCitizen_server.member.entity.AuthProvider;
import com.WhoisntCitizen_server.member.service.GoogleAuthService;
import com.WhoisntCitizen_server.member.service.KakaoAuthService;
import org.junit.jupiter.api.Test;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.boot.test.context.SpringBootTest;
import org.springframework.boot.webmvc.test.autoconfigure.AutoConfigureMockMvc;
import org.springframework.http.MediaType;
import org.springframework.test.context.bean.override.mockito.MockitoBean;
import org.springframework.test.web.servlet.MockMvc;

import static org.mockito.Mockito.verify;
import static org.mockito.Mockito.when;
import static org.springframework.test.web.servlet.request.MockMvcRequestBuilders.post;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.jsonPath;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.status;

/** 웹(WebGL) 인가 코드 로그인: 토큰 없이 접근 가능하고, 코드 교환 결과가 기존 토큰 로그인과 같은 회원 처리로 이어진다. */
@SpringBootTest
@AutoConfigureMockMvc
class MemberOAuthCodeLoginTest {

    @Autowired MockMvc mvc;
    @MockitoBean GoogleAuthService google;
    @MockitoBean KakaoAuthService kakao;

    private static OAuthUserInfo info(AuthProvider p, String id, String nickname) {
        return OAuthUserInfo.builder().provider(p).providerId(id).email("a@b.c").nickname(nickname).build();
    }

    @Test
    void 구글_인가_코드로_로그인하면_JWT가_발급된다() throws Exception {
        when(google.verifyAuthCode("auth-code")).thenReturn(info(AuthProvider.GOOGLE, "g-code-1", "구글웹유저"));

        mvc.perform(post("/api/members/login/google/code").contentType(MediaType.APPLICATION_JSON)
                        .content("{\"code\":\"auth-code\"}"))
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.username").value("google_g-code-1"))
                .andExpect(jsonPath("$.accessToken").isNotEmpty());
    }

    @Test
    void 카카오_인가_코드와_redirectUri가_서비스로_전달된다() throws Exception {
        when(kakao.verifyAuthCode("auth-code", "http://localhost:8000/kakao-callback.html"))
                .thenReturn(info(AuthProvider.KAKAO, "k-code-1", "카카오웹유저"));

        mvc.perform(post("/api/members/login/kakao/code").contentType(MediaType.APPLICATION_JSON)
                        .content("{\"code\":\"auth-code\",\"redirectUri\":\"http://localhost:8000/kakao-callback.html\"}"))
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.username").value("kakao_k-code-1"))
                .andExpect(jsonPath("$.accessToken").isNotEmpty());

        verify(kakao).verifyAuthCode("auth-code", "http://localhost:8000/kakao-callback.html");
    }

    @Test
    void 코드_교환에_실패하면_토큰이_발급되지_않는다() throws Exception {
        when(google.verifyAuthCode("bad-code")).thenThrow(new IllegalArgumentException("유효하지 않거나 만료된 구글 인가 코드입니다."));

        mvc.perform(post("/api/members/login/google/code").contentType(MediaType.APPLICATION_JSON)
                        .content("{\"code\":\"bad-code\"}"))
                .andExpect(status().is4xxClientError());
    }

    @Test
    void 코드가_비어_있으면_400() throws Exception {
        mvc.perform(post("/api/members/login/kakao/code").contentType(MediaType.APPLICATION_JSON)
                        .content("{\"code\":\"\"}"))
                .andExpect(status().isBadRequest());
    }
}
