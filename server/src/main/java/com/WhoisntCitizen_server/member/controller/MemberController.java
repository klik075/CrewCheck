package com.WhoisntCitizen_server.member.controller;

import com.WhoisntCitizen_server.member.dto.MemberDto;
import com.WhoisntCitizen_server.member.service.MemberService;
import jakarta.validation.Valid;
import lombok.RequiredArgsConstructor;
import org.springframework.http.ResponseEntity;
import org.springframework.web.bind.annotation.PostMapping;
import org.springframework.web.bind.annotation.RequestBody;
import org.springframework.web.bind.annotation.RequestMapping;
import org.springframework.web.bind.annotation.RestController;

@RestController
@RequestMapping("/api/members")
@RequiredArgsConstructor
public class MemberController {

	private final MemberService memberService;

	@PostMapping("/signup")
	public ResponseEntity<MemberDto.Response> signup(@Valid @RequestBody MemberDto.SignupRequest req) {
		return ResponseEntity.ok(memberService.signup(req));
	}

	@PostMapping("/login")
	public ResponseEntity<MemberDto.AuthResult> login(@Valid @RequestBody MemberDto.LoginRequest req) {
		return ResponseEntity.ok(memberService.login(req));
	}
}
