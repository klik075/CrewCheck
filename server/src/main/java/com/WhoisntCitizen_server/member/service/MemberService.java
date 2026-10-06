package com.WhoisntCitizen_server.member.service;

import com.WhoisntCitizen_server.global.util.JwtTokenProvider;
import com.WhoisntCitizen_server.member.dto.MemberDto;
import com.WhoisntCitizen_server.member.entity.AuthProvider;
import com.WhoisntCitizen_server.member.entity.Member;
import com.WhoisntCitizen_server.user.entity.User;
import com.WhoisntCitizen_server.member.repository.MemberRepository;
import com.WhoisntCitizen_server.user.entity.User;
import com.WhoisntCitizen_server.user.service.UserService;
import com.WhoisntCitizen_server.user.repository.UserRepository;
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
	private final UserService userService;
	private final PasswordEncoder passwordEncoder;
	private final JwtTokenProvider jwtTokenProvider;

	// 회원가입: Member(로그인 정보) + User(프로필)를 한 트랜잭션에서 같이 생성
	@Transactional
	public MemberDto.Response signup(MemberDto.SignupRequest req) {
		if (memberRepository.existsByUsername(req.getUsername())) {
			throw new IllegalArgumentException("이미 사용 중인 아이디입니다.");
		}
		if (userService.existsByNickname(req.getNickname())) {
			throw new IllegalArgumentException("이미 사용 중인 닉네임입니다.");
		}

		Member member = memberRepository.save(Member.builder()
				.username(req.getUsername())
				.password(passwordEncoder.encode(req.getPassword()))
				.build());

		User user = userService.createProfile(member.getId(), req.getNickname());

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

		User user = userService.getByMemberId(member.getId());

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

	@Transactional
	public MemberDto.AuthResult socialLogin(AuthProvider provider, String providerId, String defaultNickname) {
		// 1. 이미 연동된 회원인지 확인 (없으면 신규 등록)
		Member member = memberRepository.findByProviderAndProviderId(provider, providerId)
				.orElseGet(() -> registerSocialMember(provider, providerId, defaultNickname));

		// 2. 연관된 User 프로필 조회
		User user = userService.getByMemberId(member.getId());

		// 3. 기존과 동일한 규격으로 자체 JWT 발급
		String accessToken = jwtTokenProvider.createToken(member.getId(), member.getUsername());
		log.info("[소셜 로그인 성공] 플랫폼: {}, 회원 ID: {}, 아이디: {}", provider, member.getId(), member.getUsername());

		// 4. 기존 AuthResult 재사용
		return MemberDto.AuthResult.builder()
				.memberId(member.getId())
				.userId(user.getId())
				.username(member.getUsername())
				.nickname(user.getNickname())
				.accessToken(accessToken)
				.message(provider + " 로그인에 성공했습니다.")
				.build();
	}

	private Member registerSocialMember(AuthProvider provider, String providerId, String defaultNickname) {
		// username 중복 방지를 위한 유니크 가상 아이디 생성 (예: google_123456789)
		String generatedUsername = provider.name().toLowerCase() + "_" + providerId;

		Member member = memberRepository.save(Member.builder()
				.username(generatedUsername)
				.password(null) // 소셜 가입자는 패스워드 없음
				.provider(provider)
				.providerId(providerId)
				.build());

		// 닉네임 중복/길이 처리는 User 도메인이 담당
		User user = userService.createProfileWithUniqueNickname(member.getId(), defaultNickname);

		log.info("[소셜 회원 자동 가입 완료] 플랫폼: {}, 회원 ID: {}, 닉네임: {}", provider, member.getId(), user.getNickname());
		return member;
	}
}
