package com.WhoisntCitizen_server.member.dto;

import com.WhoisntCitizen_server.member.entity.AuthProvider;
import jakarta.validation.constraints.NotBlank;
import jakarta.validation.constraints.Pattern;
import jakarta.validation.constraints.Size;
import lombok.AllArgsConstructor;
import lombok.Builder;
import lombok.Getter;
import lombok.NoArgsConstructor;

public class MemberDto {

	@Getter
	@NoArgsConstructor
	@AllArgsConstructor
	@Builder
	public static class SignupRequest {

		@NotBlank(message = "아이디는 필수 입력값입니다.")
		@Pattern(regexp = "^[a-z0-9]{4,12}$", message = "아이디는 영문 소문자와 숫자 4~12자리여야 합니다.")
		private String username;

		@NotBlank(message = "비밀번호는 필수 입력값입니다.")
		@Pattern(
				regexp = "^(?=.*[A-Za-z])(?=.*\\d)(?=.*[!@#$%^&*])[A-Za-z\\d!@#$%^&*]{8,20}$",
				message = "비밀번호는 영문, 숫자, 특수문자(!@#$%^&*)를 포함한 8~20자리여야 합니다."
		)
		private String password;

		@NotBlank(message = "닉네임은 필수 입력값입니다.")
		@Size(min = 2, max = 10, message = "닉네임은 2~10자여야 합니다.")
		private String nickname;
	}

	@Getter
	@NoArgsConstructor
	@AllArgsConstructor
	@Builder
	public static class LoginRequest {
		@NotBlank(message = "아이디를 입력해주세요.")
		private String username;

		@NotBlank(message = "비밀번호를 입력해주세요.")
		private String password;
	}

	@Getter
	@NoArgsConstructor
	@AllArgsConstructor
	@Builder
	public static class GuestLoginRequest {
		@NotBlank(message = "게스트 식별자(UUID)는 필수 입력값입니다.")
		private String guestUuid;
	}

	@Getter
	@NoArgsConstructor
	public static class LinkSocialRequest {
		@NotBlank(message = "토큰 값은 필수입니다.")
		private String token; // 구글(idToken)이든 카카오(accessToken)든 범용으로 token 하나로 수신
	}

	@Getter
	@NoArgsConstructor
	@AllArgsConstructor
	@Builder
	public static class LinkResult {
		private Long memberId;
		private AuthProvider linkedProvider;
		private String message;
	}

	@Getter
	@NoArgsConstructor
	@AllArgsConstructor
	@Builder
	public static class Response {
		private Long memberId;
		private Long userId;
		private String username;
		private String nickname;
		private String message;
	}

	@Getter
	@NoArgsConstructor
	@AllArgsConstructor
	@Builder
	public static class AuthResult {
		private Long memberId;
		private Long userId;
		private String username;
		private String nickname;
		private String accessToken;
		private String message;
	}

}
