package com.WhoisntCitizen_server.user.controller;

import com.WhoisntCitizen_server.user.dto.UserDto;
import com.WhoisntCitizen_server.user.service.UserService;
import jakarta.validation.Valid;
import lombok.RequiredArgsConstructor;
import org.springframework.http.ResponseEntity;
import org.springframework.security.core.annotation.AuthenticationPrincipal;
import org.springframework.security.oauth2.jwt.Jwt;
import org.springframework.web.bind.annotation.*;

@RestController
@RequestMapping("/api/users")
@RequiredArgsConstructor
public class UserController {

	private final UserService userService;

	// 내 프로필 정보 조회
	@GetMapping("/me")
	public ResponseEntity<UserDto.MyProfileResponse> getMyProfile(
			@AuthenticationPrincipal Jwt jwt
	) {
		Long currentMemberId = Long.valueOf(jwt.getSubject());
		return ResponseEntity.ok(userService.getMyProfile(currentMemberId));
	}

	// 타인 정보 단건 조회 (userId 기준) - 인증 불필요 or 토큰 무관
	@GetMapping("/{userId}")
	public ResponseEntity<UserDto.PublicProfileResponse> getPublicProfile(
			@PathVariable Long userId
	) {
		return ResponseEntity.ok(userService.getPublicProfile(userId));
	}

	// 닉네임으로 유저 검색 (친구 추가 검색용)
	@GetMapping("/search")
	public ResponseEntity<UserDto.PublicProfileResponse> searchByNickname(
			@RequestParam String nickname
	) {
		return ResponseEntity.ok(userService.searchByNickname(nickname));
	}

	// 닉네임 변경
	@PatchMapping("/me/nickname")
	public ResponseEntity<UserDto.MyProfileResponse> updateNickname(
			@AuthenticationPrincipal Jwt jwt,
			@RequestBody @Valid UserDto.UpdateNicknameRequest req
	) {
		Long currentMemberId = Long.valueOf(jwt.getSubject());
		return ResponseEntity.ok(userService.updateNickname(currentMemberId, req.getNickname()));
	}
}
