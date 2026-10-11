package com.WhoisntCitizen_server.user.service;

import com.WhoisntCitizen_server.user.repository.UserRepository;
import lombok.RequiredArgsConstructor;
import org.springframework.security.oauth2.jwt.Jwt;
import org.springframework.stereotype.Component;

import java.util.concurrent.ConcurrentHashMap;

/**
 * 로그인 토큰(JWT)으로 "지금 요청한 유저"의 userId를 찾는다.
 * 토큰 sub = memberId(계정 id) -> users 테이블에서 그 계정의 User(프로필) id를 꺼낸다.
 * 게임 안의 playerId는 이 userId와 같다. (방에서 게임을 시작할 때 userId로 참가자를 넘기므로)
 *
 * 사용하는 곳: "토큰 → 요청한 사람" 변환은 이 클래스 한 곳에서만 한다.
 *   - RoomController(로비), GameController, VoteController, NightController
 *   - 컨트롤러가 userId로 바꿔서 서비스에 넘기므로, 서비스는 계정(Member)이나 memberId를 몰라도 된다.
 *   - 변환 방식을 바꿀 때(캐시, 토큰에 userId 넣기 등) 이 클래스만 고치면 된다.
 *
 * ---------------------------------------------------------------------------
 * 캐시 (memberId → userId)
 * ---------------------------------------------------------------------------
 * 왜 두나
 *   이 메서드는 클라이언트가 1초마다 보내는 polling 요청에서 불린다.
 *     - 게임 화면: GET /api/v1/games/{gameId}   (GameController.state)
 *     - 대기실   : GET /api/v1/rooms/{roomId}    (RoomController.getRoom → 대기실 접속 기록)
 *   findByMemberId는 JPA가 결과를 기억해 두지 않는 쿼리 메서드라, 캐시가 없으면 "접속자 수 × 초당 1번" SELECT가 나간다.
 *   인덱스 조회라 지금 규모에서 부담이 크지는 않지만, 바뀌지 않는 값을 매번 읽을 이유가 없다.
 *   DB가 잠깐 멈춰도 이미 접속한 사람의 polling이 계속 성공한다는 장점도 있다.
 *
 * 왜 캐시해도 안전한가
 *   - Member(계정)와 User(프로필)는 1:1이다. (users.member_id UNIQUE)
 *   - 한 번 만들어진 짝은 바뀌지 않는다. 닉네임·전적이 바뀌어도 id는 그대로다.
 *   - 지금은 계정/프로필 삭제 기능이 없다.
 *   → 값이 틀어질 일이 없으므로 만료(TTL)나 무효화 없이 계속 들고 있어도 된다.
 *   - 서버가 여러 대여도 서버마다 따로 들고 있으면 된다. (값이 같으므로 서로 맞출 필요가 없다)
 *
 * 규칙
 *   - "찾았을 때만" 저장한다. 없는 유저(예외)는 저장하지 않는다.
 *     회원가입 직후처럼 프로필이 아직 없을 때 "없음"을 기억해 버리면, 프로필이 생긴 뒤에도 계속 실패하기 때문이다.
 *   - computeIfAbsent 대신 get → (없으면) DB 조회 → put 순서로 쓴다.
 *     computeIfAbsent 안에서 DB를 부르면 그 조회가 끝날 때까지 같은 칸을 쓰는 다른 스레드가 기다린다.
 *     처음 한 번 두 스레드가 동시에 DB를 읽을 수는 있지만 결과가 같으므로 문제없다.
 *   - 크기 제한은 두지 않았다. 항목 하나가 Long 두 개라 수만 명이 접속해도 수 MB 수준이다.
 *     접속자가 아주 많아지면 Caffeine 같은 크기 제한 캐시로 바꾼다.
 *
 * ⚠ 이 캐시를 다시 봐야 하는 경우
 *   - 계정 탈퇴 / 프로필 삭제·재생성 기능을 만들면: 그 처리 끝에서 evict(memberId)를 호출해야 한다.
 *     (안 그러면 지워진 프로필의 userId를 계속 돌려준다. 서버가 여러 대면 모든 서버에 알려야 한다)
 *
 * ---------------------------------------------------------------------------
 * TODO(서버 여러 대로 늘릴 때): userId를 토큰(JWT claim)에 넣는 방식을 고려한다.
 * ---------------------------------------------------------------------------
 *   지금 캐시는 서버 메모리에 있으므로 서버가 여러 대면
 *     - 서버마다 사람당 처음 1번씩 DB를 읽고, 재시작·배포 때마다 캐시가 비어 다시 읽는다.
 *     - 위의 evict도 모든 서버에 알려야 한다.
 *   토큰 방식이면 서버가 기억할 것이 없어 이 문제들이 사라진다.
 *     1. JwtTokenProvider.createToken에 userId를 받아 claim("userId", userId)로 넣는다.
 *     2. MemberService의 토큰 발급 3곳(일반 로그인, 소셜 로그인, 소셜 자동 가입)은 이미 User를 조회한 뒤 토큰을 만들므로 user.getId()를 넘기면 된다.
 *     3. 여기서는 jwt.getClaim("userId")를 먼저 읽고, 없을 때만(배포 전에 발급된 옛 토큰, 만료 1시간 동안) 지금의 캐시/DB 조회를 쓴다.
 *   주의: 토큰은 서명만 되어 있고 암호화되지 않아 누구나 내용을 읽을 수 있다.
 *         userId는 로그인 응답·방 참가자 목록으로 이미 공개되는 값이라 넣어도 괜찮지만, 비밀 값은 넣지 않는다.
 *   주의: 토큰에 넣은 값은 만료 전까지 바꿀 수 없다. 프로필 재생성 같은 기능이 생기면 만료 시간까지 예전 userId가 쓰인다.
 *
 * TODO(구조): 이 클래스는 Jwt(웹 계층 타입)를 받으면서 DB 조회(서비스 계층 일)도 한다.
 *   나중에 @CurrentUserId + HandlerMethodArgumentResolver(웹 계층)와 UserService.getUserIdByMemberId(서비스 계층, 캐시)로 나눈다.
 */
@Component
@RequiredArgsConstructor
public class CurrentUserResolver {

    private final UserRepository userRepository;

    /**
     * memberId(토큰 sub) → userId(User.id).
     * 필드에서 바로 초기화한 final 필드라 @RequiredArgsConstructor의 생성자 인자에는 들어가지 않는다.
     * 여러 요청 스레드가 동시에 읽고 쓰므로 ConcurrentHashMap을 쓴다.
     */
    private final ConcurrentHashMap<Long, Long> userIdByMemberId = new ConcurrentHashMap<>();

    /**
     * 요청한 유저의 userId. 캐시에 있으면 DB를 읽지 않는다.
     *
     * @throws IllegalArgumentException 이 계정의 프로필(User)이 없으면 (GlobalExceptionHandler가 400으로 바꾼다)
     */
    public Long userId(Jwt jwt) {
        Long memberId = Long.valueOf(jwt.getSubject());

        // 1. 캐시 확인: 대부분의 요청(polling)은 여기서 끝난다.
        Long cached = userIdByMemberId.get(memberId);
        if (cached != null) return cached;

        // 2. 처음 보는 계정이면 DB에서 찾는다. 없으면 예외 → 캐시에 남기지 않는다.
        Long userId = userRepository.findByMemberId(memberId)
                .orElseThrow(() -> new IllegalArgumentException("존재하지 않는 유저입니다."))
                .getId();

        // 3. 찾은 값만 저장한다.
        userIdByMemberId.put(memberId, userId);
        return userId;
    }

    /**
     * 캐시에서 한 계정을 지운다. 다음 요청 때 DB에서 다시 읽는다.
     * 지금은 호출하는 곳이 없다. 계정 탈퇴·프로필 삭제/재생성 기능을 만들면 그 처리 끝에서 호출한다.
     * (서버가 여러 대면 이 서버의 캐시만 지워진다는 점에 주의. 클래스 주석의 TODO 참고)
     */
    public void evict(Long memberId) {
        if (memberId != null) userIdByMemberId.remove(memberId);
    }
}
