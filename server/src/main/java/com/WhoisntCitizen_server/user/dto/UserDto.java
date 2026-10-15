package com.WhoisntCitizen_server.user.dto;

import com.WhoisntCitizen_server.common.validation.ValidNickname;
import com.WhoisntCitizen_server.user.entity.User;
import jakarta.validation.constraints.Max;
import jakarta.validation.constraints.Min;
import jakarta.validation.constraints.NotBlank;
import jakarta.validation.constraints.Pattern;
import jakarta.validation.constraints.Size;
import lombok.AllArgsConstructor;
import lombok.Builder;
import lombok.Getter;
import lombok.NoArgsConstructor;

public class UserDto {
	/** 선택 가능한 아바타 프리셋 개수. 클라이언트 AvatarCatalog.Count 와 맞춘다. */
	public static final int PROFILE_IMAGE_COUNT = 20;

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
		private int profileImageId;

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
					.profileImageId(user.getProfileImageId())
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
		private int profileImageId;

		public static PublicProfileResponse from(User user) {
			double rate = Math.round(user.getWinRate() * 1000.0) / 10.0;

			return PublicProfileResponse.builder()
					.userId(user.getId())
					.nickname(user.getNickname())
					.level(user.getLevel())
					.playCount(user.getPlayCount())
					.winCount(user.getWinCount())
					.winRate(rate)
					.profileImageId(user.getProfileImageId())
					.build();
		}
	}

	// 닉네임 변경 요청
	@Getter
	@NoArgsConstructor
	@AllArgsConstructor
	public static class UpdateNicknameRequest {
		@NotBlank(message = "닉네임은 비어있을 수 없습니다.")
		@ValidNickname
		private String nickname;
	}

	// 프로필 이미지 변경 요청
	@Getter
	@NoArgsConstructor
	@AllArgsConstructor
	public static class UpdateProfileImageRequest {
		@Min(value = 0, message = "올바르지 않은 프로필 이미지입니다.")
		@Max(value = PROFILE_IMAGE_COUNT - 1, message = "올바르지 않은 프로필 이미지입니다.")
		private int profileImageId;
	}
}
