package com.WhoisntCitizen_server.member.service;

import com.WhoisntCitizen_server.common.exception.UnauthorizedException;
import com.WhoisntCitizen_server.global.util.JwtTokenProvider;
import com.WhoisntCitizen_server.member.dto.MemberDto;
import com.WhoisntCitizen_server.member.entity.Member;
import com.WhoisntCitizen_server.member.repository.MemberRepository;
import com.WhoisntCitizen_server.user.entity.User;
import com.WhoisntCitizen_server.user.service.UserService;
import lombok.RequiredArgsConstructor;
import lombok.extern.slf4j.Slf4j;
import org.springframework.stereotype.Service;

/** 토큰 재발급과 로그아웃(리프레시 토큰 폐기). 리프레시 토큰은 사용할 때마다 새 값으로 교체한다(Rotation). */
@Slf4j
@Service
@RequiredArgsConstructor
public class AuthService {

	private final RefreshTokenStore refreshTokenStore;
	private final JwtTokenProvider jwtTokenProvider;
	private final MemberRepository memberRepository;
	private final UserService userService;

	/**
	 * 리프레시 토큰으로 새 액세스 토큰 + 새 리프레시 토큰을 발급한다. 쓴 토큰은 바로 무효가 된다.
	 * 저장된 값과 다르거나 없으면(만료·로그아웃·다른 곳에서 새로 로그인해 교체됨) 401만 낸다.
	 * 저장된 토큰은 지우지 않는다: 다른 곳에서 로그인해 쫓겨난 이전 기기가 낡은 토큰을 보내는 일이 정상적으로 생기는데,
	 * 그때 지우면 새로 로그인한 기기의 유효한 토큰까지 사라진다.
	 */
	public MemberDto.AuthResult refresh(String refreshToken) {
		Long memberId = refreshTokenStore.parseMemberId(refreshToken)
				.orElseThrow(() -> new UnauthorizedException("유효하지 않은 리프레시 토큰입니다."));

		if (!refreshTokenStore.matches(memberId, refreshToken)) {
			log.warn("[토큰 재발급 거부] 저장된 리프레시 토큰과 불일치 또는 만료. 회원 ID: {}", memberId);
			throw new UnauthorizedException("리프레시 토큰이 만료되었거나 유효하지 않습니다. 다시 로그인해 주세요.");
		}

		Member member = memberRepository.findById(memberId)
				.orElseThrow(() -> new UnauthorizedException("존재하지 않는 회원입니다."));
		User user = userService.getByMemberId(memberId);

		// 비교와 교체를 한 번에 한다. 같은 토큰으로 동시에 온 요청 중 하나만 통과하고 나머지는 여기서 401이 된다.
		String newRefreshToken = refreshTokenStore.rotate(member.getId(), refreshToken)
				.orElseThrow(() -> new UnauthorizedException("리프레시 토큰이 만료되었거나 유효하지 않습니다. 다시 로그인해 주세요."));
		String accessToken = jwtTokenProvider.createToken(member.getId(), member.getUsername());
		log.info("[토큰 재발급] 회원 ID: {}", memberId);

		return MemberDto.AuthResult.builder()
				.memberId(member.getId())
				.userId(user.getId())
				.username(member.getUsername())
				.nickname(user.getNickname())
				.accessToken(accessToken)
				.refreshToken(newRefreshToken)
				.message("토큰이 재발급되었습니다.")
				.build();
	}

	/** 로그아웃: 저장된 리프레시 토큰과 같을 때만 폐기한다. (이전 기기의 낡은 토큰으로 새 로그인을 끊을 수 없게) */
	public void logout(String refreshToken) {
		refreshTokenStore.parseMemberId(refreshToken).ifPresent(memberId -> {
			if (refreshTokenStore.matches(memberId, refreshToken)) {
				refreshTokenStore.revoke(memberId);
				log.info("[로그아웃] 리프레시 토큰 폐기. 회원 ID: {}", memberId);
			}
		});
	}
}
