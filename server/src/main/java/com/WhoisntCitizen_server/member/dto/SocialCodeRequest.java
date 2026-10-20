package com.WhoisntCitizen_server.member.dto;

import jakarta.validation.constraints.NotBlank;
import lombok.Getter;
import lombok.NoArgsConstructor;

/**
 * 웹(WebGL) 소셜 로그인/연동 요청. 브라우저 팝업에서 받은 인가 코드(authorization code)를 그대로 받는다.
 * 서버가 코드를 구글/카카오 토큰으로 바꾼 뒤 기존 토큰 검증 로직(verifyToken)으로 합류한다.
 * redirectUri는 카카오만 쓴다. (구글 팝업 방식은 서버가 "postmessage"로 고정해서 교환한다)
 */
@Getter
@NoArgsConstructor
public class SocialCodeRequest {

	@NotBlank(message = "인가 코드는 필수입니다.")
	private String code;

	/** 카카오: 인가 코드를 받을 때 쓴 Redirect URI. 허용 목록에 있는 값이어야 하고 토큰 교환 때도 같은 값을 써야 한다. */
	private String redirectUri;
}
