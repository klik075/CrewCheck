package com.WhoisntCitizen_server.member;

import com.WhoisntCitizen_server.common.exception.UnauthorizedException;
import com.WhoisntCitizen_server.global.util.JwtTokenProvider;
import com.WhoisntCitizen_server.member.dto.MemberDto;
import com.WhoisntCitizen_server.member.entity.Member;
import com.WhoisntCitizen_server.member.repository.MemberRepository;
import com.WhoisntCitizen_server.member.service.AuthService;
import com.WhoisntCitizen_server.member.service.RefreshTokenStore;
import com.WhoisntCitizen_server.user.entity.User;
import com.WhoisntCitizen_server.user.service.UserService;
import org.junit.jupiter.api.BeforeEach;
import org.junit.jupiter.api.Test;

import java.util.Optional;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertNotNull;
import static org.junit.jupiter.api.Assertions.assertThrows;
import static org.mockito.ArgumentMatchers.anyLong;
import static org.mockito.ArgumentMatchers.anyString;
import static org.mockito.Mockito.mock;
import static org.mockito.Mockito.never;
import static org.mockito.Mockito.verify;
import static org.mockito.Mockito.when;

/** 재발급 규칙: 일치하면 새 토큰 발급(Rotation), 불일치·만료면 401 + 폐기, 로그아웃은 일치할 때만 폐기. Redis 없이 목으로 확인한다. */
class AuthServiceTest {

	private static final long MEMBER_ID = 7L;
	private static final String OLD_TOKEN = "7.old";
	private static final String NEW_TOKEN = "7.new";

	private RefreshTokenStore store;
	private AuthService authService;

	@BeforeEach
	void setUp() {
		store = mock(RefreshTokenStore.class);
		JwtTokenProvider jwt = new JwtTokenProvider(
				"c29tZS1zZWNyZXQta2V5LXZlcnktc3Ryb25nLWFuZC1zZWN1cmUtZm9yLWp3dC10b2tlbi1leGFtcGxlCg==", 3_600_000L);
		MemberRepository members = mock(MemberRepository.class);
		UserService users = mock(UserService.class);

		Member member = Member.builder().username("tester").build();
		org.springframework.test.util.ReflectionTestUtils.setField(member, "id", MEMBER_ID);
		User user = User.builder().memberId(MEMBER_ID).nickname("닉").build();
		when(members.findById(MEMBER_ID)).thenReturn(Optional.of(member));
		when(users.getByMemberId(MEMBER_ID)).thenReturn(user);

		when(store.parseMemberId(OLD_TOKEN)).thenReturn(Optional.of(MEMBER_ID));
		when(store.rotate(MEMBER_ID, OLD_TOKEN)).thenReturn(Optional.of(NEW_TOKEN));
		authService = new AuthService(store, jwt, members, users);
	}

	@Test
	void refresh_일치하면_새_토큰을_발급한다() {
		when(store.matches(MEMBER_ID, OLD_TOKEN)).thenReturn(true);

		MemberDto.AuthResult result = authService.refresh(OLD_TOKEN);

		assertNotNull(result.getAccessToken());
		assertEquals(NEW_TOKEN, result.getRefreshToken());
		verify(store).rotate(MEMBER_ID, OLD_TOKEN); // 비교와 교체를 한 번에 해서 기존 값은 무효가 된다
	}

	@Test
	void refresh_불일치하면_401이고_저장된_토큰은_건드리지_않는다() {
		when(store.matches(MEMBER_ID, OLD_TOKEN)).thenReturn(false);

		assertThrows(UnauthorizedException.class, () -> authService.refresh(OLD_TOKEN));
		verify(store, never()).revoke(MEMBER_ID); // 쫓겨난 이전 기기의 낡은 토큰이 새 로그인의 토큰을 지우면 안 된다
		verify(store, never()).rotate(anyLong(), anyString());
	}

	@Test
	void refresh_같은_토큰으로_동시에_온_요청에서_교체에_진_쪽은_401() {
		// 사전 확인(matches)은 둘 다 통과했지만 다른 요청이 먼저 교체해서 rotate가 비어 돌아오는 경우
		when(store.matches(MEMBER_ID, OLD_TOKEN)).thenReturn(true);
		when(store.rotate(MEMBER_ID, OLD_TOKEN)).thenReturn(Optional.empty());

		assertThrows(UnauthorizedException.class, () -> authService.refresh(OLD_TOKEN));
		verify(store, never()).revoke(MEMBER_ID);
	}

	@Test
	void refresh_형식이_잘못된_토큰은_401() {
		when(store.parseMemberId("garbage")).thenReturn(Optional.empty());

		assertThrows(UnauthorizedException.class, () -> authService.refresh("garbage"));
	}

	@Test
	void logout_일치할_때만_폐기한다() {
		when(store.matches(MEMBER_ID, OLD_TOKEN)).thenReturn(true);
		authService.logout(OLD_TOKEN);
		verify(store).revoke(MEMBER_ID);
	}

	@Test
	void logout_낡은_토큰이면_폐기하지_않는다() {
		when(store.matches(MEMBER_ID, OLD_TOKEN)).thenReturn(false);
		authService.logout(OLD_TOKEN);
		verify(store, never()).revoke(MEMBER_ID);
	}
}
