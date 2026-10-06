package com.WhoisntCitizen_server.member.service;

import com.WhoisntCitizen_server.member.dto.OAuthUserInfo;
import com.WhoisntCitizen_server.member.entity.AuthProvider;
import com.google.api.client.googleapis.auth.oauth2.GoogleIdToken;
import com.google.api.client.googleapis.auth.oauth2.GoogleIdTokenVerifier;
import com.google.api.client.http.javanet.NetHttpTransport;
import com.google.api.client.json.gson.GsonFactory;
import jakarta.annotation.PostConstruct;
import lombok.extern.slf4j.Slf4j;
import org.springframework.beans.factory.annotation.Value;
import org.springframework.stereotype.Service;

import java.util.Collections;

@Slf4j
@Service
public class GoogleAuthService {

	@Value("${oauth.google.client-id}")
	private String googleClientId;

	private GoogleIdTokenVerifier verifier;

	@PostConstruct
	public void init() {
		this.verifier = new GoogleIdTokenVerifier.Builder(new NetHttpTransport(), GsonFactory.getDefaultInstance())
				.setAudience(Collections.singletonList(googleClientId))
				.build();
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
