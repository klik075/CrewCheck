package com.WhoisntCitizen_server.member.entity;

import com.WhoisntCitizen_server.member.entity.Player;
import jakarta.persistence.*;
import lombok.*;

import java.util.ArrayList;
import java.util.List;

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

	@Builder.Default
	@OneToMany(mappedBy = "member", cascade = CascadeType.ALL, orphanRemoval = true)
	private List<Player> players = new ArrayList<>();

	public void addPlayer(Player player) {
		players.add(player);
		player.assignMember(this);
	}
}