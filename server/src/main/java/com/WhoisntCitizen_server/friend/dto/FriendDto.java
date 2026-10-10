package com.WhoisntCitizen_server.friend.dto;

import com.WhoisntCitizen_server.friend.entity.Friendship;
import com.WhoisntCitizen_server.user.entity.User;
import jakarta.validation.constraints.NotNull;
import lombok.*;

import java.time.LocalDateTime;

public class FriendDto {

	// 친구 요청 전송 DTO (요청 보낼 상대방의 userId 또는 닉네임)
	@Getter
	@NoArgsConstructor
	@AllArgsConstructor
	public static class FriendRequest {
		@NotNull(message = "대상 유저 ID는 필수입니다.")
		private Long targetUserId;
	}

	// 내 친구 목록 응답 (ACCEPTED 상태)
	@Getter
	@NoArgsConstructor
	@AllArgsConstructor
	@Builder
	public static class FriendResponse {
		private Long friendshipId;
		private Long friendUserId;
		private String nickname;
		private int level;
		private int playCount;
		private int winCount;
		private double winRate;

		public static FriendResponse from(Friendship friendship, User friendUser) {
			double rate = Math.round(friendUser.getWinRate() * 1000.0) / 10.0;

			return FriendResponse.builder()
					.friendshipId(friendship.getId())
					.friendUserId(friendUser.getId())
					.nickname(friendUser.getNickname())
					.level(friendUser.getLevel())
					.playCount(friendUser.getPlayCount())
					.winCount(friendUser.getWinCount())
					.winRate(rate)
					.build();
		}
	}

	// 내가 받은 대기 중인 친구 요청 응답 (PENDING 상태)
	@Getter
	@NoArgsConstructor
	@AllArgsConstructor
	@Builder
	public static class PendingRequestResponse {
		private Long friendshipId;
		private Long requesterUserId;
		private String requesterNickname;
		private LocalDateTime requestedAt;

		public static PendingRequestResponse from(Friendship friendship) {
			User requester = friendship.getRequester();
			return PendingRequestResponse.builder()
					.friendshipId(friendship.getId())
					.requesterUserId(requester.getId())
					.requesterNickname(requester.getNickname())
					.requestedAt(friendship.getCreatedAt())
					.build();
		}
	}
}
