package com.WhoisntCitizen_server.friend.repository;

import com.WhoisntCitizen_server.friend.entity.Friendship;
import com.WhoisntCitizen_server.friend.entity.FriendshipStatus;
import com.WhoisntCitizen_server.user.entity.User;
import org.springframework.data.jpa.repository.JpaRepository;
import org.springframework.data.jpa.repository.Query;
import org.springframework.data.repository.query.Param;

import java.util.List;
import java.util.Optional;

public interface FriendshipRepository extends JpaRepository<Friendship, Long> {

	// 두 유저 사이의 기존 관계 조회 (A->B 또는 B->A)
	@Query("SELECT f FROM Friendship f " +
			"WHERE (f.requester = :userA AND f.receiver = :userB) " +
			"   OR (f.requester = :userB AND f.receiver = :userA)")
	Optional<Friendship> findRelationBetween(
			@Param("userA") User userA,
			@Param("userB") User userB
	);

	// 내가 받은 대기 중인 친구 요청 목록 조회 (PENDING 상태)
	@Query("SELECT f FROM Friendship f JOIN FETCH f.requester " +
			"WHERE f.receiver = :user AND f.status = :status " +
			"ORDER BY f.createdAt DESC")
	List<Friendship> findPendingRequestsReceived(
			@Param("user") User user,
			@Param("status") FriendshipStatus status
	);

	// 수락 완료(ACCEPTED)된 내 모든 친구 관계 목록 (A가 나이거나 B가 나인 경우)
	@Query("SELECT f FROM Friendship f " +
			"JOIN FETCH f.requester " +
			"JOIN FETCH f.receiver " +
			"WHERE (f.requester = :user OR f.receiver = :user) " +
			"  AND f.status = :status")
	List<Friendship> findAllAcceptedFriends(
			@Param("user") User user,
			@Param("status") FriendshipStatus status
	);
}
