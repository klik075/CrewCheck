package com.WhoisntCitizen_server.friend.service;

import com.WhoisntCitizen_server.friend.dto.FriendDto;
import com.WhoisntCitizen_server.friend.entity.Friendship;
import com.WhoisntCitizen_server.friend.entity.FriendshipStatus;
import com.WhoisntCitizen_server.friend.repository.FriendshipRepository;
import com.WhoisntCitizen_server.user.entity.User;
import com.WhoisntCitizen_server.user.repository.UserRepository;
import lombok.RequiredArgsConstructor;
import lombok.extern.slf4j.Slf4j;
import org.springframework.stereotype.Service;
import org.springframework.transaction.annotation.Transactional;

import java.util.List;
import java.util.Objects;

@Slf4j
@Service
@RequiredArgsConstructor
@Transactional(readOnly = true)
public class FriendService {

	private final FriendshipRepository friendshipRepository;
	private final UserRepository userRepository;

	/** 친구 요청 전송 */
	@Transactional
	public void sendFriendRequest(Long currentMemberId, Long targetUserId) {
		User requester = getUserByMemberId(currentMemberId);
		User receiver = userRepository.findById(targetUserId)
				.orElseThrow(() -> new IllegalArgumentException("대상 유저를 찾을 수 없습니다."));

		// 1. 자기 자신에게 요청 방지
		if (Objects.equals(requester.getId(), receiver.getId())) {
			throw new IllegalArgumentException("자기 자신에게 친구 요청을 보낼 수 없습니다.");
		}

		// 2. 이미 존재하는 관계(요청 대기 중 or 이미 친구) 검증
		friendshipRepository.findRelationBetween(requester, receiver)
				.ifPresent(existing -> {
					if (existing.getStatus() == FriendshipStatus.ACCEPTED) {
						throw new IllegalStateException("이미 친구 관계인 유저입니다.");
					}
					if (existing.getStatus() == FriendshipStatus.PENDING) {
						throw new IllegalStateException("이미 대기 중인 친구 요청이 존재합니다.");
					}
				});

		Friendship friendship = Friendship.builder()
				.requester(requester)
				.receiver(receiver)
				.status(FriendshipStatus.PENDING)
				.build();

		friendshipRepository.save(friendship);
		log.info("[친구 요청 전송] Requester: {}, Receiver: {}", requester.getNickname(), receiver.getNickname());
	}

	/** 내가 받은 대기 중인 친구 요청 목록 조회 */
	public List<FriendDto.PendingRequestResponse> getPendingRequests(Long currentMemberId) {
		User me = getUserByMemberId(currentMemberId);
		List<Friendship> requests = friendshipRepository.findPendingRequestsReceived(me, FriendshipStatus.PENDING);

		return requests.stream()
				.map(FriendDto.PendingRequestResponse::from)
				.toList();
	}

	/** 친구 요청 수락 */
	@Transactional
	public void acceptFriendRequest(Long currentMemberId, Long friendshipId) {
		User me = getUserByMemberId(currentMemberId);
		Friendship friendship = friendshipRepository.findById(friendshipId)
				.orElseThrow(() -> new IllegalArgumentException("해당 친구 요청을 찾을 수 없습니다."));

		// 수신자가 본인인지 검증
		if (!Objects.equals(friendship.getReceiver().getId(), me.getId())) {
			throw new IllegalArgumentException("본인에게 온 친구 요청만 수락할 수 있습니다.");
		}

		if (friendship.getStatus() != FriendshipStatus.PENDING) {
			throw new IllegalStateException("이미 처리된 요청입니다.");
		}

		friendship.accept();
		log.info("[친구 요청 수락] FriendshipId: {}, 수락자: {}", friendshipId, me.getNickname());
	}

	/** 친구 요청 거절 또는 기존 친구 삭제 (양쪽 모두 가능) */
	@Transactional
	public void removeFriendship(Long currentMemberId, Long friendshipId) {
		User me = getUserByMemberId(currentMemberId);
		Friendship friendship = friendshipRepository.findById(friendshipId)
				.orElseThrow(() -> new IllegalArgumentException("해당 친구 관계를 찾을 수 없습니다."));

		// 본인이 관련된 관계인지 검증 (요청자이거나 수신자여야 함)
		boolean isRelated = Objects.equals(friendship.getRequester().getId(), me.getId())
				|| Objects.equals(friendship.getReceiver().getId(), me.getId());

		if (!isRelated) {
			throw new IllegalArgumentException("삭제 권한이 없는 친구 관계입니다.");
		}

		friendshipRepository.delete(friendship);
		log.info("[친구 관계 삭제/거절] FriendshipId: {}, 실행자: {}", friendshipId, me.getNickname());
	}

	/** 내 친구 목록 조회 (수락 완료된 상태) */
	public List<FriendDto.FriendResponse> getMyFriends(Long currentMemberId) {
		User me = getUserByMemberId(currentMemberId);
		List<Friendship> friendships = friendshipRepository.findAllAcceptedFriends(me, FriendshipStatus.ACCEPTED);

		return friendships.stream()
				.map(f -> {
					// 양방향 관계이므로 내가 아닌 상대방 유저 객체를 추출
					User friendUser = Objects.equals(f.getRequester().getId(), me.getId())
							? f.getReceiver()
							: f.getRequester();
					return FriendDto.FriendResponse.from(f, friendUser);
				})
				.toList();
	}

	private User getUserByMemberId(Long memberId) {
		return userRepository.findByMemberId(memberId)
				.orElseThrow(() -> new IllegalStateException("유저 프로필이 존재하지 않습니다."));
	}
}
