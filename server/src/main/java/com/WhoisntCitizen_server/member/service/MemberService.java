package com.WhoisntCitizen_server.member.service;

import com.WhoisntCitizen_server.global.util.JwtTokenProvider;
import com.WhoisntCitizen_server.member.dto.MemberDto;
import com.WhoisntCitizen_server.member.entity.Member;
import com.WhoisntCitizen_server.member.repository.MemberRepository;
import lombok.RequiredArgsConstructor;
import lombok.extern.slf4j.Slf4j;
import org.springframework.security.crypto.password.PasswordEncoder;
import org.springframework.stereotype.Service;
import org.springframework.transaction.annotation.Transactional;

@Slf4j
@Service
@RequiredArgsConstructor
public class MemberService {

	private final MemberRepository memberRepository;
	private final PasswordEncoder passwordEncoder;
	private final JwtTokenProvider jwtTokenProvider;

	@Transactional
	public MemberDto.Response signup(MemberDto.SignupRequest req) {
		if (memberRepository.existsByUsername(req.getUsername())) {
			throw new IllegalArgumentException("이미 사용 중인 아이디입니다.");
		}

		String encodedPassword = passwordEncoder.encode(req.getPassword());

		Member member = Member.builder()
				.username(req.getUsername())
				.password(encodedPassword)
				.build();

		Member saved = memberRepository.save(member);
		log.info("[회원가입 완료] 회원 ID: {}, 아이디: {}", saved.getId(), saved.getUsername());

		return MemberDto.Response.builder()
				.memberId(saved.getId())
				.username(saved.getUsername())
				.message("회원가입에 성공했습니다.")
				.build();
	}

	@Transactional(readOnly = true)
	public  MemberDto.AuthResult login(MemberDto.LoginRequest req) {
		Member member = memberRepository.findByUsername(req.getUsername())
				.orElseThrow(() -> new IllegalArgumentException("아이디 또는 비밀번호가 일치하지 않습니다."));

		if (!passwordEncoder.matches(req.getPassword(), member.getPassword())) {
			throw new IllegalArgumentException("아이디 또는 비밀번호가 일치하지 않습니다.");
		}

		String accessToken = jwtTokenProvider.createToken(member.getId(), member.getUsername());
		log.info("[로그인 성공] 회원 ID: {}, 아이디: {}", member.getId(), member.getUsername());

		return MemberDto.AuthResult.builder()
				.memberId(member.getId())
				.username(member.getUsername())
				.accessToken(accessToken)
				.message("로그인에 성공했습니다.")
				.build();
	}
}
