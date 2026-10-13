package com.WhoisntCitizen_server.user.entity;

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

	public void updateScore(int level, int gold) {
		this.level = level;
		this.gold = gold;
	}

	// 게임 종료 시 호출
	public void recordGame(boolean win) {
		this.playCount++;
		if (win) this.winCount++;
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
