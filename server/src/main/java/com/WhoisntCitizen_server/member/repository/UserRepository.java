package com.WhoisntCitizen_server.member.repository;

import com.WhoisntCitizen_server.member.entity.User;
import org.springframework.data.jpa.repository.JpaRepository;

import java.util.Optional;

public interface UserRepository extends JpaRepository<User, Long> {
	Optional<User> findByMemberId(Long memberId);
	boolean existsByNickname(String nickname);
}
