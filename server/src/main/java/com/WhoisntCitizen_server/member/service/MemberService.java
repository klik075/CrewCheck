package com.WhoisntCitizen_server.member.service;

import com.WhoisntCitizen_server.global.util.JwtTokenProvider;
import com.WhoisntCitizen_server.member.dto.MemberDto;
import com.WhoisntCitizen_server.member.entity.Member;
import com.WhoisntCitizen_server.member.entity.User;
import com.WhoisntCitizen_server.member.repository.MemberRepository;
import com.WhoisntCitizen_server.member.repository.UserRepository;
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
	private final UserRepository userRepository;
	private final PasswordEncoder passwordEncoder;
	private final JwtTokenProvider jwtTokenProvider;

	// 회원가입: Member(로그인 정보) + User(프로필)를 한 트랜잭션에서 같이 생성
	@Transactional
	public MemberDto.Response signup(MemberDto.SignupRequest req) {
		if (memberRepository.existsByUsername(req.getUsername())) {
			throw new IllegalArgumentException("이미 사용 중인 아이디입니다.");
		}
		if (userRepository.existsByNickname(req.getNickname())) {
			throw new IllegalArgumentException("이미 사용 중인 닉네임입니다.");
		}

		Member member = memberRepository.save(Member.builder()
				.username(req.getUsername())
				.password(passwordEncoder.encode(req.getPassword()))
				.build());

		User user = userRepository.save(User.builder()
				.member(member)
				.nickname(req.getNickname())
				.build());

		log.info("[회원가입 완료] 회원 ID: {}, 유저 ID: {}, 아이디: {}", member.getId(), user.getId(), member.getUsername());

		return MemberDto.Response.builder()
				.memberId(member.getId())
				.userId(user.getId())
				.username(member.getUsername())
				.nickname(user.getNickname())
				.message("회원가입에 성공했습니다.")
				.build();
	}

	@Transactional(readOnly = true)
	public MemberDto.AuthResult login(MemberDto.LoginRequest req) {
		Member member = memberRepository.findByUsername(req.getUsername())
				.orElseThrow(() -> new IllegalArgumentException("아이디 또는 비밀번호가 일치하지 않습니다."));

		if (!passwordEncoder.matches(req.getPassword(), member.getPassword())) {
			throw new IllegalArgumentException("아이디 또는 비밀번호가 일치하지 않습니다.");
		}

		User user = userRepository.findByMemberId(member.getId())
				.orElseThrow(() -> new IllegalStateException("유저 프로필이 존재하지 않습니다."));

		String accessToken = jwtTokenProvider.createToken(member.getId(), member.getUsername());
		log.info("[로그인 성공] 회원 ID: {}, 아이디: {}", member.getId(), member.getUsername());

		return MemberDto.AuthResult.builder()
				.memberId(member.getId())
				.userId(user.getId())
				.username(member.getUsername())
				.nickname(user.getNickname())
				.accessToken(accessToken)
				.message("로그인에 성공했습니다.")
				.build();
	}
}
