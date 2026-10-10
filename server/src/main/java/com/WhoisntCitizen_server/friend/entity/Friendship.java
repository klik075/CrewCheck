package com.WhoisntCitizen_server.friend.entity;

import com.WhoisntCitizen_server.user.entity.User;
import jakarta.persistence.*;
import lombok.*;
import org.springframework.data.annotation.CreatedDate;
import org.springframework.data.jpa.domain.support.AuditingEntityListener;

import java.time.LocalDateTime;

@Entity
@Table(
		name = "friendships",
		uniqueConstraints = {
				@UniqueConstraint(
						name = "uk_friendship_requester_receiver",
						columnNames = {"requester_id", "receiver_id"}
				)
		}
)
@EntityListeners(AuditingEntityListener.class)
@Getter
@NoArgsConstructor(access = AccessLevel.PROTECTED)
@AllArgsConstructor
@Builder
public class Friendship {

	@Id
	@GeneratedValue(strategy = GenerationType.IDENTITY)
	private Long id;

	// 요청을 보낸 유저
	@ManyToOne(fetch = FetchType.LAZY)
	@JoinColumn(name = "requester_id", nullable = false)
	private User requester;

	// 요청을 받은 유저
	@ManyToOne(fetch = FetchType.LAZY)
	@JoinColumn(name = "receiver_id", nullable = false)
	private User receiver;

	@Enumerated(EnumType.STRING)
	@Column(nullable = false, length = 20)
	private FriendshipStatus status;

	@CreatedDate
	@Column(nullable = false, updatable = false)
	private LocalDateTime createdAt;

	// 비즈니스 메서드: 요청 수락
	public void accept() {
		this.status = FriendshipStatus.ACCEPTED;
	}
}
