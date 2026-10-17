package com.WhoisntCitizen_server.member.service;

import com.WhoisntCitizen_server.member.dto.KakaoUserResponse;
import com.WhoisntCitizen_server.member.dto.OAuthUserInfo;
import com.WhoisntCitizen_server.member.entity.AuthProvider;
import lombok.extern.slf4j.Slf4j;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.beans.factory.annotation.Value;
import org.springframework.core.ParameterizedTypeReference;
import org.springframework.http.HttpStatusCode;
import org.springframework.http.MediaType;
import org.springframework.stereotype.Service;
import org.springframework.util.LinkedMultiValueMap;
import org.springframework.util.MultiValueMap;
import org.springframework.web.client.RestClient;

import java.util.Arrays;
import java.util.List;
import java.util.Map;

@Slf4j
@Service
public class KakaoAuthService {

	private final RestClient restClient;
	private final String userInfoUri;
	private final String tokenUri;
	private final String restApiKey;
	private final String clientSecret;
	private final List<String> allowedRedirectUris;

	@Autowired
	public KakaoAuthService(
			@Value("${oauth.kakao.user-info-uri:https://kapi.kakao.com/v2/user/me}") String userInfoUri,
			@Value("${oauth.kakao.token-uri:https://kauth.kakao.com/oauth/token}") String tokenUri,
			@Value("${oauth.kakao.rest-api-key:}") String restApiKey,
			@Value("${oauth.kakao.client-secret:}") String clientSecret,
			@Value("${oauth.kakao.allowed-redirect-uris:}") String allowedRedirectUris) {
		this(userInfoUri, tokenUri, restApiKey, clientSecret, allowedRedirectUris, RestClient.builder());
	}

	// 테스트용: 가짜 서버(MockRestServiceServer)를 붙인 RestClient.Builder를 넘긴다.
	KakaoAuthService(String userInfoUri, String tokenUri, String restApiKey, String clientSecret,
			String allowedRedirectUris, RestClient.Builder restClientBuilder) {
		this.restClient = restClientBuilder.build();
		this.userInfoUri = userInfoUri;
		this.tokenUri = tokenUri;
		this.restApiKey = restApiKey;
		this.clientSecret = clientSecret;
		this.allowedRedirectUris = Arrays.stream(allowedRedirectUris.split(","))
				.map(String::trim)
				.filter(s -> !s.isEmpty())
				.toList();
	}

	/**
	 * 웹(WebGL) 로그인: 팝업에서 받은 인가 코드를 카카오 access token으로 바꾼 뒤,
	 * 모바일과 같은 verifyToken으로 검증한다. (이후 회원 처리는 모바일 경로와 동일)
	 */
	public OAuthUserInfo verifyAuthCode(String code, String redirectUri) {
		return verifyToken(exchangeCodeForAccessToken(code, redirectUri));
	}

	String exchangeCodeForAccessToken(String code, String redirectUri) {
		if (restApiKey == null || restApiKey.isBlank()) {
			throw new IllegalStateException("서버에 oauth.kakao.rest-api-key(KAKAO_REST_API_KEY)가 설정되지 않았습니다.");
		}
		// 클라이언트가 보낸 redirectUri를 그대로 믿지 않는다. 미리 등록한 주소만 허용한다.
		if (redirectUri == null || !allowedRedirectUris.contains(redirectUri)) {
			throw new IllegalArgumentException("허용되지 않은 redirectUri 입니다.");
		}

		MultiValueMap<String, String> form = new LinkedMultiValueMap<>();
		form.add("grant_type", "authorization_code");
		form.add("client_id", restApiKey);
		form.add("redirect_uri", redirectUri);
		form.add("code", code);
		if (clientSecret != null && !clientSecret.isBlank()) {
			form.add("client_secret", clientSecret);
		}

		try {
			Map<String, Object> body = restClient.post()
					.uri(tokenUri)
					.contentType(MediaType.APPLICATION_FORM_URLENCODED)
					.body(form)
					.retrieve()
					.onStatus(HttpStatusCode::is4xxClientError, (req, res) -> {
						throw new IllegalArgumentException("유효하지 않거나 만료된 카카오 인가 코드입니다.");
					})
					.body(new ParameterizedTypeReference<Map<String, Object>>() {});

			Object accessToken = body == null ? null : body.get("access_token");
			if (!(accessToken instanceof String s) || s.isBlank()) {
				throw new IllegalArgumentException("카카오 응답에 access token이 없습니다.");
			}
			return s;
		} catch (IllegalArgumentException e) {
			throw e;
		} catch (Exception e) {
			log.error("[Kakao 코드 교환 실패] 토큰 엔드포인트 통신 중 오류 발생", e);
			throw new IllegalArgumentException("카카오 인가 코드 교환에 실패했습니다: " + e.getMessage());
		}
	}

	public OAuthUserInfo verifyToken(String kakaoAccessToken) {
		try {
			KakaoUserResponse response = restClient.get()
					.uri(userInfoUri)
					.header("Authorization", "Bearer " + kakaoAccessToken)
					.header("Content-type", "application/x-www-form-urlencoded;charset=utf-8")
					.retrieve()
					.onStatus(HttpStatusCode::is4xxClientError, (req, res) -> {
						throw new IllegalArgumentException("유효하지 않거나 만료된 카카오 토큰입니다.");
					})
					.body(KakaoUserResponse.class);

			if (response == null || response.getId() == null) {
				throw new IllegalArgumentException("카카오 사용자 정보를 가져올 수 없습니다.");
			}

			String providerId = String.valueOf(response.getId());
			String nickname = "KakaoUser";
			String email = null;

			if (response.getKakaoAccount() != null) {
				email = response.getKakaoAccount().getEmail();
				if (response.getKakaoAccount().getProfile() != null
						&& response.getKakaoAccount().getProfile().getNickname() != null) {
					nickname = response.getKakaoAccount().getProfile().getNickname();
				}
			}

			log.info("[Kakao 토큰 검증 성공] providerId: {}, nickname: {}", providerId, nickname);

			return OAuthUserInfo.builder()
					.providerId(providerId)
					.provider(AuthProvider.KAKAO)
					.email(email)
					.nickname(nickname)
					.build();

		} catch (IllegalArgumentException e) {
			throw e;
		} catch (Exception e) {
			log.error("[Kakao 토큰 검증 실패] API 통신 중 오류 발생", e);
			throw new IllegalArgumentException("카카오 인증 처리에 실패했습니다: " + e.getMessage());
		}
	}
}
