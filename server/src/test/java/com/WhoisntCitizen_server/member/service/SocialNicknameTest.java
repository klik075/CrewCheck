package com.WhoisntCitizen_server.member.service;

import com.WhoisntCitizen_server.member.entity.AuthProvider;
import org.junit.jupiter.api.Test;

import static org.assertj.core.api.Assertions.assertThat;

/** 소셜 가입 닉네임: 구글은 G, 카카오는 K + 숫자(자릿수만큼 앞을 0으로 채움). */
class SocialNicknameTest {

    @Test
    void 구글은_G_카카오는_K로_시작한다() {
        assertThat(MemberService.buildSocialNickname(AuthProvider.GOOGLE, 6, 48213)).isEqualTo("G048213");
        assertThat(MemberService.buildSocialNickname(AuthProvider.KAKAO, 6, 7)).isEqualTo("K000007");
    }

    @Test
    void 자릿수가_바뀌면_닉네임_길이도_바뀐다() {
        assertThat(MemberService.buildSocialNickname(AuthProvider.GOOGLE, 4, 12)).isEqualTo("G0012");
        assertThat(MemberService.buildSocialNickname(AuthProvider.GOOGLE, 19, 1)).hasSize(20); // DB 최대 길이
    }
}
