package com.WhoisntCitizen_server.member.service;

import com.WhoisntCitizen_server.member.dto.OAuthUserInfo;
import com.WhoisntCitizen_server.member.entity.AuthProvider;
import com.google.api.client.googleapis.auth.oauth2.GoogleIdToken;
import com.google.api.client.googleapis.auth.oauth2.GoogleIdTokenVerifier;
import com.google.api.client.http.javanet.NetHttpTransport;
import com.google.api.client.json.gson.GsonFactory;
import lombok.extern.slf4j.Slf4j;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.beans.factory.annotation.Value;
import org.springframework.core.ParameterizedTypeReference;
import org.springframework.http.MediaType;
import org.springframework.http.HttpStatusCode;
import org.springframework.stereotype.Service;
import org.springframework.util.LinkedMultiValueMap;
import org.springframework.util.MultiValueMap;
import org.springframework.web.client.RestClient;

import java.util.Collections;
import java.util.Map;

@Slf4j
@Service
public class GoogleAuthService {

	/** 구글 팝업(GIS initCodeClient) 방식으로 받은 인가 코드를 교환할 때 쓰는 고정 redirect_uri */
	static final String POPUP_REDIRECT_URI = "postmessage";

	private final String googleClientId;
	private final String googleClientSecret;
	private final String tokenUri;
	private final RestClient restClient;
	private final GoogleIdTokenVerifier verifier;

	@Autowired
	public GoogleAuthService(
			@Value("${oauth.google.client-id}") String googleClientId,
			@Value("${oauth.google.client-secret:}") String googleClientSecret,
			@Value("${oauth.google.token-uri:https://oauth2.googleapis.com/token}") String tokenUri) {
		this(googleClientId, googleClientSecret, tokenUri, RestClient.builder());
	}

	// 테스트용: 가짜 서버(MockRestServiceServer)를 붙인 RestClient.Builder를 넘긴다.
	GoogleAuthService(String googleClientId, String googleClientSecret, String tokenUri, RestClient.Builder restClientBuilder) {
		this.googleClientId = googleClientId;
		this.googleClientSecret = googleClientSecret;
		this.tokenUri = tokenUri;
		this.restClient = restClientBuilder.build();
		this.verifier = new GoogleIdTokenVerifier.Builder(new NetHttpTransport(), GsonFactory.getDefaultInstance())
				.setAudience(Collections.singletonList(googleClientId))
				.build();
	}

	/**
	 * 웹(WebGL) 로그인: 팝업에서 받은 인가 코드를 구글 토큰 엔드포인트에서 ID 토큰으로 바꾼 뒤,
	 * 모바일과 같은 verifyToken으로 검증한다. (이후 회원 처리는 모바일 경로와 동일)
	 */
	public OAuthUserInfo verifyAuthCode(String code) {
		return verifyToken(exchangeCodeForIdToken(code));
	}

	String exchangeCodeForIdToken(String code) {
		if (googleClientSecret == null || googleClientSecret.isBlank()) {
			throw new IllegalStateException("서버에 oauth.google.client-secret(GOOGLE_CLIENT_SECRET)이 설정되지 않았습니다.");
		}

		MultiValueMap<String, String> form = new LinkedMultiValueMap<>();
		form.add("code", code);
		form.add("client_id", googleClientId);
		form.add("client_secret", googleClientSecret);
		form.add("redirect_uri", POPUP_REDIRECT_URI);
		form.add("grant_type", "authorization_code");

		try {
			Map<String, Object> body = restClient.post()
					.uri(tokenUri)
					.contentType(MediaType.APPLICATION_FORM_URLENCODED)
					.body(form)
					.retrieve()
					.onStatus(HttpStatusCode::is4xxClientError, (req, res) -> {
						throw new IllegalArgumentException("유효하지 않거나 만료된 구글 인가 코드입니다.");
					})
					.body(new ParameterizedTypeReference<Map<String, Object>>() {});

			Object idToken = body == null ? null : body.get("id_token");
			if (!(idToken instanceof String s) || s.isBlank()) {
				throw new IllegalArgumentException("구글 응답에 ID 토큰이 없습니다. (scope에 openid가 필요합니다)");
			}
			return s;
		} catch (IllegalArgumentException e) {
			throw e;
		} catch (Exception e) {
			log.error("[Google 코드 교환 실패] 토큰 엔드포인트 통신 중 오류 발생", e);
			throw new IllegalArgumentException("구글 인가 코드 교환에 실패했습니다: " + e.getMessage());
		}
	}

	public OAuthUserInfo verifyToken(String idTokenString) {
		try {
			GoogleIdToken idToken = verifier.verify(idTokenString);
			if (idToken == null) {
				throw new IllegalArgumentException("유효하지 않거나 위조된 구글 ID 토큰입니다.");
			}

			GoogleIdToken.Payload payload = idToken.getPayload();

			String providerId = payload.getSubject();
			String email = payload.getEmail();
			String name = (String) payload.get("name");

			String nickname = (name != null && !name.isBlank()) ? name : "GoogleUser";

			log.info("[Google 토큰 검증 성공] providerId: {}, email: {}", providerId, email);

			return OAuthUserInfo.builder()
					.providerId(providerId)
					.provider(AuthProvider.GOOGLE)
					.email(email)
					.nickname(nickname)
					.build();

		} catch (IllegalArgumentException e) {
			throw e;
		} catch (Exception e) {
			log.error("[Google 토큰 검증 실패] 토큰 처리 중 오류 발생", e);
			throw new IllegalArgumentException("구글 인증 처리에 실패했습니다: " + e.getMessage());
		}
	}
}
