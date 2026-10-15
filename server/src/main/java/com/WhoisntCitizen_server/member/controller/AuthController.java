package com.WhoisntCitizen_server.member.controller;

import com.WhoisntCitizen_server.member.dto.MemberDto;
import com.WhoisntCitizen_server.member.service.AuthService;
import jakarta.validation.Valid;
import lombok.RequiredArgsConstructor;
import org.springframework.http.ResponseEntity;
import org.springframework.web.bind.annotation.PostMapping;
import org.springframework.web.bind.annotation.RequestBody;
import org.springframework.web.bind.annotation.RequestMapping;
import org.springframework.web.bind.annotation.RestController;

/** 토큰 재발급 / 로그아웃. 액세스 토큰이 만료돼도 호출할 수 있도록 Authorization 헤더 없이 body의 리프레시 토큰으로 인증한다. */
@RestController
@RequestMapping("/api/auth")
@RequiredArgsConstructor
public class AuthController {

	private final AuthService authService;

	@PostMapping("/refresh")
	public ResponseEntity<MemberDto.AuthResult> refresh(@Valid @RequestBody MemberDto.RefreshRequest req) {
		return ResponseEntity.ok(authService.refresh(req.getRefreshToken()));
	}

	@PostMapping("/logout")
	public ResponseEntity<Void> logout(@Valid @RequestBody MemberDto.RefreshRequest req) {
		authService.logout(req.getRefreshToken());
		return ResponseEntity.noContent().build();
	}
}
