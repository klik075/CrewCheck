package com.WhoisntCitizen_server.user.entity;

import com.WhoisntCitizen_server.jobs.domain.Faction;
import jakarta.persistence.*;
import lombok.*;

/**
 * 유저 프로필 (닉네임, 전적 등). 로비/게임에서는 Member 대신 이 객체를 사용한다.
 * Member(로그인 계정)와는 memberId 값으로만 연결한다. (엔티티 직접 참조 없음 -> 도메인 분리)
 * 테이블명은 예약어 user 를 피해서 users.
 */
@Entity
@Table(name = "users")
@Getter
@NoArgsConstructor(access = AccessLevel.PROTECTED)
@AllArgsConstructor
@Builder
public class User {

	@Id
	@GeneratedValue(strategy = GenerationType.IDENTITY)
	private Long id;

	@Column(name = "member_id", nullable = false, unique = true)
	private Long memberId;

	@Column(nullable = false, unique = true, length = 20)
	private String nickname;

	private int level;
	private int gold;

	// 전적 (승률은 저장하지 않고 계산)
	private int playCount;
	private int winCount;

	// 진영별 전적 (선원/해적/중립). 전체 전적과 별개로 게임 종료 때 함께 올린다.
	private int crewPlayCount;
	private int crewWinCount;
	private int piratePlayCount;
	private int pirateWinCount;
	private int neutralPlayCount;
	private int neutralWinCount;

	// 아바타 프리셋 번호 (0 ~ UserDto.PROFILE_IMAGE_COUNT-1). 이미지 파일이 아니라 클라이언트 프리셋의 번호만 저장한다.
	@Builder.Default
	private int profileImageId = 0;

	public void updateProfileImage(int profileImageId) {
		this.profileImageId = profileImageId;
	}

	public void updateScore(int level, int gold) {
		this.level = level;
		this.gold = gold;
	}

	// 게임 종료 시 호출
	public void recordGame(Faction faction, boolean win) {
		this.playCount++;
		if (win) this.winCount++;

		if (faction == null) return; // 진영을 모르면 전체 전적만 올린다
		switch (faction) {
			case CREW -> {
				this.crewPlayCount++;
				if (win) this.crewWinCount++;
			}
			case PIRATE -> {
				this.piratePlayCount++;
				if (win) this.pirateWinCount++;
			}
			case NEUTRAL -> {
				this.neutralPlayCount++;
				if (win) this.neutralWinCount++;
			}
		}
	}

	public double getWinRate() {
		return playCount == 0 ? 0.0 : (double) winCount / playCount;
	}

	public void updateNickname(String newNickname) {
		if (newNickname != null && !newNickname.isBlank()) {
			this.nickname = newNickname;
		}
	}
}
