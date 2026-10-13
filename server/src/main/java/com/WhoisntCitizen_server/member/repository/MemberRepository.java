package com.WhoisntCitizen_server.member.repository;

import com.WhoisntCitizen_server.member.entity.AuthProvider;
import com.WhoisntCitizen_server.member.entity.Member;
import org.springframework.data.jpa.repository.JpaRepository;

import java.util.Optional;

public interface MemberRepository extends JpaRepository<Member, Long> {
	boolean existsByUsername(String username);
	Optional<Member> findByUsername(String username);
	Optional<Member> findByProviderAndProviderId(AuthProvider provider, String providerId);
}