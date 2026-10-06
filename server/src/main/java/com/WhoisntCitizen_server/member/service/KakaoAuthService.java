package com.WhoisntCitizen_server.member.service;

import com.WhoisntCitizen_server.member.dto.KakaoUserResponse;
import com.WhoisntCitizen_server.member.dto.OAuthUserInfo;
import com.WhoisntCitizen_server.member.entity.AuthProvider;
import lombok.extern.slf4j.Slf4j;
import org.springframework.beans.factory.annotation.Value;
import org.springframework.http.HttpStatusCode;
import org.springframework.stereotype.Service;
import org.springframework.web.client.RestClient;

@Slf4j
@Service
public class KakaoAuthService {

	private final RestClient restClient;
	private final String userInfoUri;

	public KakaoAuthService(@Value("${oauth.kakao.user-info-uri:https://kapi.kakao.com/v2/user/me}") String userInfoUri) {
		this.restClient = RestClient.builder().build();
		this.userInfoUri = userInfoUri;
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
