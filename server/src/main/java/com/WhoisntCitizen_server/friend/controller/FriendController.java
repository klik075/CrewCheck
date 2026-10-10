package com.WhoisntCitizen_server.friend.controller;

import com.WhoisntCitizen_server.friend.dto.FriendDto;
import com.WhoisntCitizen_server.friend.service.FriendService;
import jakarta.validation.Valid;
import lombok.RequiredArgsConstructor;
import org.springframework.http.HttpStatus;
import org.springframework.http.ResponseEntity;
import org.springframework.security.core.annotation.AuthenticationPrincipal;
import org.springframework.security.oauth2.jwt.Jwt;
import org.springframework.web.bind.annotation.*;

import java.util.List;

@RestController
@RequestMapping("/api/friends")
@RequiredArgsConstructor
public class FriendController {

	private final FriendService friendService;

	/**
	 * 내 친구 목록 조회
	 */
	@GetMapping
	public ResponseEntity<List<FriendDto.FriendResponse>> getMyFriends(
			@AuthenticationPrincipal Jwt jwt
	) {
		Long currentMemberId = Long.valueOf(jwt.getSubject());
		return ResponseEntity.ok(friendService.getMyFriends(currentMemberId));
	}

	/**
	 * 내가 받은 대기 중인 친구 요청 목록 조회
	 */
	@GetMapping("/requests")
	public ResponseEntity<List<FriendDto.PendingRequestResponse>> getPendingRequests(
			@AuthenticationPrincipal Jwt jwt
	) {
		Long currentMemberId = Long.valueOf(jwt.getSubject());
		return ResponseEntity.ok(friendService.getPendingRequests(currentMemberId));
	}

	/**
	 * 친구 요청 전송
	 */
	@PostMapping("/request")
	public ResponseEntity<Void> sendFriendRequest(
			@AuthenticationPrincipal Jwt jwt,
			@RequestBody @Valid FriendDto.FriendRequest req
	) {
		Long currentMemberId = Long.valueOf(jwt.getSubject());
		friendService.sendFriendRequest(currentMemberId, req.getTargetUserId());
		return ResponseEntity.status(HttpStatus.CREATED).build();
	}

	/**
	 * 친구 요청 수락
	 */
	@PostMapping("/{friendshipId}/accept")
	public ResponseEntity<Void> acceptFriendRequest(
			@AuthenticationPrincipal Jwt jwt,
			@PathVariable Long friendshipId
	) {
		Long currentMemberId = Long.valueOf(jwt.getSubject());
		friendService.acceptFriendRequest(currentMemberId, friendshipId);
		return ResponseEntity.ok().build();
	}

	/**
	 * 친구 요청 거절 또는 기존 친구 삭제
	 */
	@DeleteMapping("/{friendshipId}")
	public ResponseEntity<Void> removeFriendship(
			@AuthenticationPrincipal Jwt jwt,
			@PathVariable Long friendshipId
	) {
		Long currentMemberId = Long.valueOf(jwt.getSubject());
		friendService.removeFriendship(currentMemberId, friendshipId);
		return ResponseEntity.noContent().build();
	}
}
