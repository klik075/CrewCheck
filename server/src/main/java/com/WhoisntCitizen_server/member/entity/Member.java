package com.WhoisntCitizen_server.member.entity;

import jakarta.persistence.*;
import lombok.*;

/**
 * 로그인(인증) 전용 엔티티. 비밀번호 같은 민감 정보만 가진다.
 * 닉네임, 전적 같은 프로필 정보는 User 에 있다. (User -> Member 단방향 1:1)
 */
@Entity
@Table(name = "members")
@Getter
@NoArgsConstructor(access = AccessLevel.PROTECTED)
@AllArgsConstructor
@Builder
public class Member {

	@Id
	@GeneratedValue(strategy = GenerationType.IDENTITY)
	private Long id;

	@Column(nullable = false, unique = true, length = 50)
	private String username;

	@Column(nullable = false)
	private String password;
}
