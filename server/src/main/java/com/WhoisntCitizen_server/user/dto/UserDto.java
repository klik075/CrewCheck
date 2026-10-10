package com.WhoisntCitizen_server.user.dto;

import com.WhoisntCitizen_server.user.entity.User;
import jakarta.validation.constraints.NotBlank;
import jakarta.validation.constraints.Pattern;
import jakarta.validation.constraints.Size;
import lombok.AllArgsConstructor;
import lombok.Builder;
import lombok.Getter;
import lombok.NoArgsConstructor;

public class UserDto {
	// 내 프로필 조회 응답 (로비 상단 UI 및 내 정보창)
	@Getter
	@NoArgsConstructor
	@AllArgsConstructor
	@Builder
	public static class MyProfileResponse {
		private Long userId;
		private Long memberId;
		private String nickname;
		private int level;
		private int gold;
		private int playCount;
		private int winCount;
		private int lossCount;
		private double winRate; // 백분율 (예: 62.5%)

		public static MyProfileResponse from(User user) {
			int loss = Math.max(0, user.getPlayCount() - user.getWinCount());
			double rate = Math.round(user.getWinRate() * 1000.0) / 10.0;

			return MyProfileResponse.builder()
					.userId(user.getId())
					.memberId(user.getMemberId())
					.nickname(user.getNickname())
					.level(user.getLevel())
					.gold(user.getGold())
					.playCount(user.getPlayCount())
					.winCount(user.getWinCount())
					.lossCount(loss)
					.winRate(rate)
					.build();
		}
	}

	// 타인 프로필 조회 응답 (친구 검색, 룸 클릭 시 - 골드 제외)
	@Getter
	@NoArgsConstructor
	@AllArgsConstructor
	@Builder
	public static class PublicProfileResponse {
		private Long userId;
		private String nickname;
		private int level;
		private int playCount;
		private int winCount;
		private double winRate;

		public static PublicProfileResponse from(User user) {
			double rate = Math.round(user.getWinRate() * 1000.0) / 10.0;

			return PublicProfileResponse.builder()
					.userId(user.getId())
					.nickname(user.getNickname())
					.level(user.getLevel())
					.playCount(user.getPlayCount())
					.winCount(user.getWinCount())
					.winRate(rate)
					.build();
		}
	}

	// 닉네임 변경 요청
	@Getter
	@NoArgsConstructor
	@AllArgsConstructor
	public static class UpdateNicknameRequest {
		@NotBlank(message = "닉네임은 비어있을 수 없습니다.")
		@Size(min = 2, max = 20, message = "닉네임은 2자 이상 20자 이하여야 합니다.")
		@Pattern(regexp = "^[a-zA-Z0-9가-힣]+$", message = "닉네임은 한글, 영문, 숫자만 사용 가능합니다.")
		private String nickname;
	}
}
