package com.WhoisntCitizen_server.member.service;

import com.WhoisntCitizen_server.global.util.JwtTokenProvider;
import com.WhoisntCitizen_server.member.dto.MemberDto;
import com.WhoisntCitizen_server.realtime.RealtimeNotifier;
import com.WhoisntCitizen_server.realtime.protocol.RealtimeProtocol;
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
	private final RefreshTokenStore refreshTokenStore;
	private final RealtimeNotifier realtimeNotifier;

	/**
	 * 로그인할 때 리프레시 토큰을 새로 발급한다. 같은 계정의 이전 리프레시 토큰은 이 발급으로 덮어써져 무효가 되고,
	 * 이전 기기의 실시간 연결(WebSocket)은 여기서 닫는다(4409). 클라이언트는 이 코드를 받으면 로그아웃 처리한다.
	 * (아직 새 기기의 연결은 맺기 전이므로 새 로그인의 연결이 끊기지는 않는다)
	 */
	private String issueRefreshToken(Member member, User user) {
		String refreshToken = refreshTokenStore.issue(member.getId());
		realtimeNotifier.disconnectUser(user.getId(), RealtimeProtocol.CLOSE_DUPLICATE_LOGIN, "다른 곳에서 로그인했습니다.");
		return refreshToken;
	}

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
				.refreshToken(issueRefreshToken(member, user))
				.message("로그인에 성공했습니다.")
				.build();
	}

	@Transactional
	public MemberDto.AuthResult socialLogin(AuthProvider provider, String providerId) {
		Member member = memberRepository.findByProviderAndProviderId(provider, providerId)
				.orElseGet(() -> registerSocialMember(provider, providerId));

		User user = userService.getByMemberId(member.getId());

		String accessToken = jwtTokenProvider.createToken(member.getId(), member.getUsername());
		log.info("[소셜 로그인 성공] 플랫폼: {}, 회원 ID: {}, 아이디: {}", provider, member.getId(), member.getUsername());

		return MemberDto.AuthResult.builder()
				.memberId(member.getId())
				.userId(user.getId())
				.username(member.getUsername())
				.nickname(user.getNickname())
				.accessToken(accessToken)
				.refreshToken(issueRefreshToken(member, user))
				.message(provider + " 로그인에 성공했습니다.")
				.build();
	}

	/** 소셜 가입 닉네임의 숫자 자릿수. 닉네임 = 구글 "G", 카카오 "K" + 숫자. 바꾸려면 이 값만 고친다. (DB 닉네임 최대 20자라 19 이하) */
	private static final int SOCIAL_NICKNAME_DIGITS = 6;

	/** 접두어(G/K) + 무작위 숫자. 숫자는 자릿수를 채우도록 앞을 0으로 채운다. 예) G048213 */
	static String buildSocialNickname(AuthProvider provider, int digits, long randomValue) {
		String prefix = provider == AuthProvider.GOOGLE ? "G" : provider == AuthProvider.KAKAO ? "K" : "U";
		return prefix + String.format("%0" + digits + "d", randomValue);
	}

	private String generateUniqueSocialNickname(AuthProvider provider) {
		int digits = Math.max(1, Math.min(SOCIAL_NICKNAME_DIGITS, 19));
		long bound = (long) Math.pow(10, digits);
		String nickname;
		do {
			nickname = buildSocialNickname(provider, digits, java.util.concurrent.ThreadLocalRandom.current().nextLong(bound));
		} while (userRepository.existsByNickname(nickname));
		return nickname;
	}

	private Member registerSocialMember(AuthProvider provider, String providerId) {
		String generatedUsername = provider.name().toLowerCase() + "_" + providerId;

		Member member = memberRepository.save(Member.builder()
				.username(generatedUsername)
				.password(null)
				.provider(provider)
				.providerId(providerId)
				.build());

		// 구글/카카오 계정의 이름은 쓰지 않는다. (실명 노출 방지, 길이·중복 문제 방지)
		User user = userService.createProfile(member.getId(), generateUniqueSocialNickname(provider));

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
				.refreshToken(issueRefreshToken(member, user))
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
