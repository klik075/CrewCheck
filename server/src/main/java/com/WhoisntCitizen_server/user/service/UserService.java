package com.WhoisntCitizen_server.user.service;

import com.WhoisntCitizen_server.user.dto.UserDto;
import com.WhoisntCitizen_server.user.entity.User;
import com.WhoisntCitizen_server.user.repository.UserRepository;
import lombok.RequiredArgsConstructor;
import org.springframework.stereotype.Service;
import org.springframework.transaction.annotation.Transactional;

/** 유저 프로필(닉네임, 전적) 생성/조회. Member(로그인 계정) 도메인은 이 서비스를 통해서만 User를 다룬다. */
@Service
@RequiredArgsConstructor
@Transactional(readOnly = true)
public class UserService {

	private static final int MAX_NICKNAME_LENGTH = 20; // users.nickname VARCHAR(20)

	private final UserRepository userRepository;

	public boolean existsByNickname(String nickname) {
		return userRepository.existsByNickname(nickname);
	}

	@Transactional(readOnly = true)
	public User getByMemberId(Long memberId) {
		return userRepository.findByMemberId(memberId)
				.orElseThrow(() -> new IllegalStateException("유저 프로필이 존재하지 않습니다."));
	}

	/** 닉네임이 이미 있으면 IllegalArgumentException (일반 회원가입용). */
	@Transactional
	public User createProfile(Long memberId, String nickname) {
		if (userRepository.existsByNickname(nickname)) {
			throw new IllegalArgumentException("이미 사용 중인 닉네임입니다.");
		}
		return userRepository.save(User.builder().memberId(memberId).nickname(nickname).build());
	}

	/** 소셜 가입용: 닉네임이 겹치거나 길면 자르고 숫자를 붙여 유일한 닉네임으로 만든다. */
	@Transactional
	public User createProfileWithUniqueNickname(Long memberId, String desiredNickname) {
		String base = (desiredNickname == null || desiredNickname.isBlank()) ? "User" : desiredNickname.strip();
		String nickname = truncate(base, MAX_NICKNAME_LENGTH);
		while (userRepository.existsByNickname(nickname)) {
			String suffix = "_" + (int) (Math.random() * 9000 + 1000);
			nickname = truncate(base, MAX_NICKNAME_LENGTH - suffix.length()) + suffix;
		}
		return userRepository.save(User.builder().memberId(memberId).nickname(nickname).build());
	}

	private static String truncate(String s, int max) {
		return s.length() <= max ? s : s.substring(0, max);
	}

	/** 내 프로필 상세 조회 (로비 진입 시 호출) */
	public UserDto.MyProfileResponse getMyProfile(Long currentMemberId) {
		User user = getByMemberId(currentMemberId);
		return UserDto.MyProfileResponse.from(user);
	}

	/** 타인 프로필 단건 조회 (userId 기준) */
	public UserDto.PublicProfileResponse getPublicProfile(Long userId) {
		User user = userRepository.findById(userId)
				.orElseThrow(() -> new IllegalArgumentException("존재하지 않는 유저입니다."));
		return UserDto.PublicProfileResponse.from(user);
	}

	/** 닉네임으로 타인 검색 (친구 추가 검색용) */
	public UserDto.PublicProfileResponse searchByNickname(String nickname) {
		User user = userRepository.findByNickname(nickname)
				.orElseThrow(() -> new IllegalArgumentException("해당 닉네임의 유저를 찾을 수 없습니다."));
		return UserDto.PublicProfileResponse.from(user);
	}

	/** 닉네임 변경 */
	@Transactional
	public UserDto.MyProfileResponse updateNickname(Long currentMemberId, String newNickname) {
		User user = getByMemberId(currentMemberId);

		// 기존 닉네임과 동일한 경우 변경 없이 반환
		if (user.getNickname().equals(newNickname)) {
			return UserDto.MyProfileResponse.from(user);
		}

		// 다른 유저가 사용 중인 닉네임인지 검증
		if (userRepository.existsByNickname(newNickname)) {
			throw new IllegalStateException("이미 사용 중인 닉네임입니다.");
		}

		user.updateNickname(newNickname);
		return UserDto.MyProfileResponse.from(user);
	}
}
