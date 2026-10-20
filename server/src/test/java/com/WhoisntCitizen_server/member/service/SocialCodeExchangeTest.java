package com.WhoisntCitizen_server.member.service;

import com.WhoisntCitizen_server.member.dto.OAuthUserInfo;
import org.junit.jupiter.api.BeforeEach;
import org.junit.jupiter.api.Test;
import org.springframework.http.HttpMethod;
import org.springframework.http.HttpStatus;
import org.springframework.http.MediaType;
import org.springframework.test.web.client.MockRestServiceServer;
import org.springframework.web.client.RestClient;

import java.util.Map;

import static org.assertj.core.api.Assertions.assertThat;
import static org.assertj.core.api.Assertions.assertThatThrownBy;
import static org.springframework.test.web.client.match.MockRestRequestMatchers.content;
import static org.springframework.test.web.client.match.MockRestRequestMatchers.header;
import static org.springframework.test.web.client.match.MockRestRequestMatchers.method;
import static org.springframework.test.web.client.match.MockRestRequestMatchers.requestTo;
import static org.springframework.test.web.client.response.MockRestResponseCreators.withStatus;
import static org.springframework.test.web.client.response.MockRestResponseCreators.withSuccess;

/** 인가 코드 → 토큰 교환: 구글/카카오 서버는 호출하지 않고 가짜 서버(MockRestServiceServer)로 요청 내용과 응답 처리를 확인한다. */
class SocialCodeExchangeTest {

    private static final String KAKAO_TOKEN_URI = "https://kauth.test/oauth/token";
    private static final String KAKAO_USER_URI = "https://kapi.test/v2/user/me";
    private static final String GOOGLE_TOKEN_URI = "https://google.test/token";
    private static final String REDIRECT = "http://localhost:8000/kakao-callback.html";

    private RestClient.Builder kakaoBuilder;
    private MockRestServiceServer kakaoServer;
    private KakaoAuthService kakao;

    private RestClient.Builder googleBuilder;
    private MockRestServiceServer googleServer;

    @BeforeEach
    void setUp() {
        kakaoBuilder = RestClient.builder();
        kakaoServer = MockRestServiceServer.bindTo(kakaoBuilder).build();
        kakao = new KakaoAuthService(KAKAO_USER_URI, KAKAO_TOKEN_URI, "rest-key", "kakao-secret", REDIRECT, kakaoBuilder);

        googleBuilder = RestClient.builder();
        googleServer = MockRestServiceServer.bindTo(googleBuilder).build();
    }

    private GoogleAuthService google(String secret) {
        return new GoogleAuthService("client-id.apps.googleusercontent.com", secret, GOOGLE_TOKEN_URI, googleBuilder);
    }

    @Test
    void 카카오_코드를_토큰으로_바꾸고_사용자_정보를_조회한다() {
        kakaoServer.expect(requestTo(KAKAO_TOKEN_URI))
                .andExpect(method(HttpMethod.POST))
                .andExpect(content().formDataContains(Map.of(
                        "grant_type", "authorization_code",
                        "client_id", "rest-key",
                        "client_secret", "kakao-secret",
                        "redirect_uri", REDIRECT,
                        "code", "the-code")))
                .andRespond(withSuccess("{\"access_token\":\"AT\"}", MediaType.APPLICATION_JSON));
        kakaoServer.expect(requestTo(KAKAO_USER_URI))
                .andExpect(header("Authorization", "Bearer AT"))
                .andRespond(withSuccess("{\"id\":123,\"kakao_account\":{\"profile\":{\"nickname\":\"웹유저\"}}}",
                        MediaType.APPLICATION_JSON));

        OAuthUserInfo info = kakao.verifyAuthCode("the-code", REDIRECT);

        assertThat(info.getProviderId()).isEqualTo("123");
        assertThat(info.getNickname()).isEqualTo("웹유저");
        kakaoServer.verify();
    }

    @Test
    void 등록되지_않은_redirectUri는_카카오를_호출하기_전에_거절한다() {
        assertThatThrownBy(() -> kakao.verifyAuthCode("the-code", "https://evil.example/cb"))
                .isInstanceOf(IllegalArgumentException.class);
        assertThatThrownBy(() -> kakao.verifyAuthCode("the-code", null))
                .isInstanceOf(IllegalArgumentException.class);
        kakaoServer.verify(); // 요청이 하나도 나가지 않았어야 한다
    }

    @Test
    void 카카오가_코드를_거절하면_로그인이_실패한다() {
        kakaoServer.expect(requestTo(KAKAO_TOKEN_URI)).andRespond(withStatus(HttpStatus.BAD_REQUEST));

        assertThatThrownBy(() -> kakao.verifyAuthCode("used-code", REDIRECT))
                .isInstanceOf(IllegalArgumentException.class)
                .hasMessageContaining("인가 코드");
    }

    @Test
    void mock_토큰도_우회되지_않고_카카오에_그대로_검증을_요청한다() {
        kakaoServer.expect(requestTo(KAKAO_USER_URI)).andRespond(withStatus(HttpStatus.UNAUTHORIZED));

        assertThatThrownBy(() -> kakao.verifyToken("mock_anything"))
                .isInstanceOf(IllegalArgumentException.class);
        kakaoServer.verify(); // 검증 요청이 실제로 나갔어야 한다 (우회 없음)
    }

    @Test
    void 카카오_REST_키가_없으면_설정_오류로_알린다() {
        KakaoAuthService noKey = new KakaoAuthService(KAKAO_USER_URI, KAKAO_TOKEN_URI, "", "", REDIRECT, kakaoBuilder);

        assertThatThrownBy(() -> noKey.verifyAuthCode("the-code", REDIRECT))
                .isInstanceOf(IllegalStateException.class);
    }

    @Test
    void 구글_코드는_postmessage로_교환해_ID토큰을_받는다() {
        googleServer.expect(requestTo(GOOGLE_TOKEN_URI))
                .andExpect(method(HttpMethod.POST))
                .andExpect(content().formDataContains(Map.of(
                        "grant_type", "authorization_code",
                        "client_id", "client-id.apps.googleusercontent.com",
                        "client_secret", "google-secret",
                        "redirect_uri", "postmessage",
                        "code", "the-code")))
                .andRespond(withSuccess("{\"id_token\":\"the-id-token\",\"access_token\":\"x\"}", MediaType.APPLICATION_JSON));

        // ID 토큰 서명 검증(verifyToken)은 실제 구글 키가 필요해서, 여기서는 교환 단계가 올바른 값을 꺼내는지만 확인한다.
        String idToken = google("google-secret").exchangeCodeForIdToken("the-code");

        assertThat(idToken).isEqualTo("the-id-token");
        googleServer.verify();
    }

    @Test
    void 구글이_ID토큰을_주지_않으면_실패한다() {
        googleServer.expect(requestTo(GOOGLE_TOKEN_URI))
                .andRespond(withSuccess("{\"access_token\":\"x\"}", MediaType.APPLICATION_JSON));

        assertThatThrownBy(() -> google("google-secret").verifyAuthCode("the-code"))
                .isInstanceOf(IllegalArgumentException.class)
                .hasMessageContaining("ID 토큰");
    }

    @Test
    void 구글이_코드를_거절하면_로그인이_실패한다() {
        googleServer.expect(requestTo(GOOGLE_TOKEN_URI)).andRespond(withStatus(HttpStatus.BAD_REQUEST));

        assertThatThrownBy(() -> google("google-secret").verifyAuthCode("used-code"))
                .isInstanceOf(IllegalArgumentException.class)
                .hasMessageContaining("인가 코드");
    }

    @Test
    void 구글_클라이언트_시크릿이_없으면_설정_오류로_알린다() {
        assertThatThrownBy(() -> google("").verifyAuthCode("the-code"))
                .isInstanceOf(IllegalStateException.class);
    }
}
