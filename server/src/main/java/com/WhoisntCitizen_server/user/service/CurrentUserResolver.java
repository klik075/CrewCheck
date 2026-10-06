package com.WhoisntCitizen_server.user.service;

import com.WhoisntCitizen_server.user.repository.UserRepository;
import lombok.RequiredArgsConstructor;
import org.springframework.security.oauth2.jwt.Jwt;
import org.springframework.stereotype.Component;

/**
 * 로그인 토큰(JWT)으로 "지금 요청한 유저"의 userId를 찾는다.
 * 토큰 sub = memberId(계정 id) -> users 테이블에서 그 계정의 User(프로필) id를 꺼낸다.
 * 게임 안의 playerId는 이 userId와 같다. (방에서 게임을 시작할 때 userId로 참가자를 넘기므로)
 */
@Component
@RequiredArgsConstructor
public class CurrentUserResolver {

    private final UserRepository userRepository;

    public Long userId(Jwt jwt) {
        Long memberId = Long.valueOf(jwt.getSubject());
        return userRepository.findByMemberId(memberId)
                .orElseThrow(() -> new IllegalArgumentException("존재하지 않는 유저입니다."))
                .getId();
    }
}
