package com.WhoisntCitizen_server.member.service;

import com.WhoisntCitizen_server.global.util.JwtTokenProvider;
import com.WhoisntCitizen_server.member.dto.MemberDto;
import com.WhoisntCitizen_server.member.entity.AuthProvider;
import com.WhoisntCitizen_server.member.entity.Member;
import com.WhoisntCitizen_server.user.entity.User;
import com.WhoisntCitizen_server.member.repository.MemberRepository;
import com.WhoisntCitizen_server.user.service.UserService;
import com.WhoisntCitizen_server.user.repository.UserRepository;
import lombok.RequiredArgsConstructor;
import lombok.extern.slf4j.Slf4j;
import org.springframework.security.crypto.password.PasswordEncoder;
import org.springframework.stereotype.Service;
import org.springframework.transaction.annotation.Transactional;

import java.util.Optional;

@Slf4j
@Service
@RequiredArgsConstructor
public class MemberService {

	private final MemberRepository memberRepository;
	private final UserService userService;
	private final PasswordEncoder passwordEncoder;
	private final JwtTokenProvider jwtTokenProvider;
	private final UserRepository userRepository;

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
		Member member = memberRepository.findByProviderAndProviderId(provider, providerId)
				.orElseGet(() -> registerSocialMember(provider, providerId, defaultNickname));

		User user = userService.getByMemberId(member.getId());

		String accessToken = jwtTokenProvider.createToken(member.getId(), member.getUsername());
		log.info("[소셜 로그인 성공] 플랫폼: {}, 회원 ID: {}, 아이디: {}", provider, member.getId(), member.getUsername());

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
		String generatedUsername = provider.name().toLowerCase() + "_" + providerId;

		Member member = memberRepository.save(Member.builder()
				.username(generatedUsername)
				.password(null)
				.provider(provider)
				.providerId(providerId)
				.build());

		User user = userService.createProfileWithUniqueNickname(member.getId(), defaultNickname);

		log.info("[소셜 회원 자동 가입 완료] 플랫폼: {}, 회원 ID: {}, 닉네임: {}", provider, member.getId(), user.getNickname());
		return member;
	}

	@Transactional
	public MemberDto.AuthResult guestLogin(MemberDto.GuestLoginRequest req) {
		String guestUuid = req.getGuestUuid();

		Member member = memberRepository.findByProviderAndProviderId(AuthProvider.GUEST, guestUuid)
				.orElseGet(() -> registerGuestMember(guestUuid));

		User user = userRepository.findByMemberId(member.getId())
				.orElseThrow(() -> new IllegalStateException("게스트 유저 프로필이 존재하지 않습니다."));

		String accessToken = jwtTokenProvider.createToken(member.getId(), member.getUsername());
		log.info("[게스트 로그인 성공] 회원 ID: {}, 식별자: {}", member.getId(), guestUuid);

		return MemberDto.AuthResult.builder()
				.memberId(member.getId())
				.userId(user.getId())
				.username(member.getUsername())
				.nickname(user.getNickname())
				.accessToken(accessToken)
				.message("게스트 로그인에 성공했습니다.")
				.build();
	}

	private Member registerGuestMember(String guestUuid) {
		String generatedUsername = "guest_" + guestUuid;

		Member member = memberRepository.save(Member.builder()
				.username(generatedUsername)
				.password(null)
				.provider(AuthProvider.GUEST)
				.providerId(guestUuid)
				.build());

		String nickname = generateUniqueGuestNickname();

		User user = userRepository.save(User.builder()
				.memberId(member.getId())
				.nickname(nickname)
				.build());

		log.info("[신규 게스트 생성 완료] 회원 ID: {}, 닉네임: {}", member.getId(), user.getNickname());
		return member;
	}

	private String generateUniqueGuestNickname() {
		String nickname;
		do {
			int randomSuffix = (int) (Math.random() * 9000) + 1000;
			nickname = "게스트" + randomSuffix;
		} while (userRepository.existsByNickname(nickname));
		return nickname;
	}

	@Transactional
	public MemberDto.LinkResult linkSocialAccount(Long memberId, AuthProvider provider, String providerId) {
		Optional<Member> existingMember = memberRepository.findByProviderAndProviderId(provider, providerId);
		if (existingMember.isPresent()) {
			if (existingMember.get().getId().equals(memberId)) {
				throw new IllegalArgumentException("이미 현재 계정에 연동된 소셜 계정입니다.");
			}
			throw new IllegalStateException("이미 다른 계정에 연동되어 있는 소셜 계정입니다.");
		}

		Member currentMember = memberRepository.findById(memberId)
				.orElseThrow(() -> new IllegalArgumentException("존재하지 않는 회원입니다."));

		if (currentMember.getProvider() == AuthProvider.GOOGLE || currentMember.getProvider() == AuthProvider.KAKAO) {
			throw new IllegalStateException("이미 " + currentMember.getProvider() + " 계정으로 연동이 완료된 상태입니다.");
		}

		currentMember.linkSocialAccount(provider, providerId);
		log.info("[소셜 계정 연동 완료] 회원 ID: {}, 연동 플랫폼: {}, 식별자: {}", memberId, provider, providerId);

		return MemberDto.LinkResult.builder()
				.memberId(currentMember.getId())
				.linkedProvider(provider)
				.message(provider + " 계정 연동이 완료되었습니다.")
				.build();
	}
}
