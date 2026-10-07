package com.WhoisntCitizen_server.member.controller;

import com.WhoisntCitizen_server.member.dto.MemberDto;
import com.WhoisntCitizen_server.member.dto.OAuthUserInfo;
import com.WhoisntCitizen_server.member.dto.SocialLoginRequest;
import com.WhoisntCitizen_server.member.entity.AuthProvider;
import com.WhoisntCitizen_server.member.service.GoogleAuthService;
import com.WhoisntCitizen_server.member.service.KakaoAuthService;
import com.WhoisntCitizen_server.member.service.MemberService;
import jakarta.validation.Valid;
import lombok.RequiredArgsConstructor;
import org.springframework.http.ResponseEntity;
import org.springframework.security.core.annotation.AuthenticationPrincipal;
import org.springframework.web.bind.annotation.PostMapping;
import org.springframework.web.bind.annotation.RequestBody;
import org.springframework.web.bind.annotation.RequestMapping;
import org.springframework.web.bind.annotation.RestController;

@RestController
@RequestMapping("/api/members")
@RequiredArgsConstructor
public class MemberController {

	private final MemberService memberService;
	private final GoogleAuthService googleAuthService;
	private final KakaoAuthService kakaoAuthService;

	@PostMapping("/signup")
	public ResponseEntity<MemberDto.Response> signup(@Valid @RequestBody MemberDto.SignupRequest req) {
		return ResponseEntity.ok(memberService.signup(req));
	}

	@PostMapping("/login")
	public ResponseEntity<MemberDto.AuthResult> login(@Valid @RequestBody MemberDto.LoginRequest req) {
		return ResponseEntity.ok(memberService.login(req));
	}

	@PostMapping("/login/google")
	public ResponseEntity<MemberDto.AuthResult> googleLogin(@Valid @RequestBody SocialLoginRequest req) {
		OAuthUserInfo info = googleAuthService.verifyToken(req.getToken());
		return ResponseEntity.ok(memberService.socialLogin(info.getProvider(), info.getProviderId(), info.getNickname()));
	}

	@PostMapping("/login/kakao")
	public ResponseEntity<MemberDto.AuthResult> kakaoLogin(@Valid @RequestBody SocialLoginRequest req) {
		OAuthUserInfo info = kakaoAuthService.verifyToken(req.getToken());
		return ResponseEntity.ok(memberService.socialLogin(info.getProvider(), info.getProviderId(), info.getNickname()));
	}

	@PostMapping("/guest")
	public ResponseEntity<MemberDto.AuthResult> loginGuest(
			@RequestBody @Valid MemberDto.GuestLoginRequest req
	) {
		MemberDto.AuthResult result = memberService.guestLogin(req);
		return ResponseEntity.ok(result);
	}

	@PostMapping("/link/google")
	public ResponseEntity<MemberDto.LinkResult> linkGoogle(
			@AuthenticationPrincipal Long currentMemberId,
			@RequestBody @Valid MemberDto.LinkSocialRequest req
	) {
		OAuthUserInfo userInfo = googleAuthService.verifyToken(req.getToken());
		MemberDto.LinkResult result = memberService.linkSocialAccount(
				currentMemberId, AuthProvider.GOOGLE, userInfo.getProviderId()
		);
		return ResponseEntity.ok(result);
	}

	@PostMapping("/link/kakao")
	public ResponseEntity<MemberDto.LinkResult> linkKakao(
			@AuthenticationPrincipal Long currentMemberId,
			@RequestBody @Valid MemberDto.LinkSocialRequest req
	) {
		OAuthUserInfo userInfo = kakaoAuthService.verifyToken(req.getToken());
		MemberDto.LinkResult result = memberService.linkSocialAccount(
				currentMemberId, AuthProvider.KAKAO, userInfo.getProviderId()
		);
		return ResponseEntity.ok(result);
	}
}
