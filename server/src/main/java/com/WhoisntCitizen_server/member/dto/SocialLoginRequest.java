package com.WhoisntCitizen_server.member.dto;

import jakarta.validation.constraints.NotBlank;
import lombok.Getter;
import lombok.NoArgsConstructor;

@Getter
@NoArgsConstructor
public class SocialLoginRequest {

	@NotBlank(message = "소셜 토큰은 필수입니다.")
	private String token;
}
