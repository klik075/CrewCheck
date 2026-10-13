package com.WhoisntCitizen_server.member.dto;

import com.fasterxml.jackson.annotation.JsonIgnoreProperties;
import com.fasterxml.jackson.annotation.JsonProperty;
import lombok.Getter;
import lombok.NoArgsConstructor;

@Getter
@NoArgsConstructor
@JsonIgnoreProperties(ignoreUnknown = true)
public class KakaoUserResponse {

	private Long id;

	@JsonProperty("kakao_account")
	private KakaoAccount kakaoAccount;

	@Getter
	@NoArgsConstructor
	@JsonIgnoreProperties(ignoreUnknown = true)
	public static class KakaoAccount {
		private String email;
		private Profile profile;

		@Getter
		@NoArgsConstructor
		@JsonIgnoreProperties(ignoreUnknown = true)
		public static class Profile {
			private String nickname;
			@JsonProperty("profile_image_url")
			private String profileImageUrl;
		}
	}
}
