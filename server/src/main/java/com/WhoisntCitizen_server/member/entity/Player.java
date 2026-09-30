package com.WhoisntCitizen_server.member.entity;

import com.WhoisntCitizen_server.member.entity.Member;
import jakarta.persistence.*;
import lombok.*;

@Entity
@Table(name = "players")
@Getter
@NoArgsConstructor(access = AccessLevel.PROTECTED)
@AllArgsConstructor
@Builder
public class Player {

	@Id
	@GeneratedValue(strategy = GenerationType.IDENTITY)
	private Long id;

	@Column(nullable = false, unique = true)
	private String nickname;

	private int level;
	private int gold;

	@ManyToOne(fetch = FetchType.LAZY)
	@JoinColumn(name = "member_id", nullable = false)
	private Member member;

	public void updateScore(int level, int gold) {
		this.level = level;
		this.gold = gold;
	}

	public void assignMember(Member member) {
		this.member = member;
	}
}