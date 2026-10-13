package com.WhoisntCitizen_server.realtime;

import com.WhoisntCitizen_server.game.activity.PlayerActivityTracker;
import com.WhoisntCitizen_server.lobby.repository.LobbyRoomRepository;
import com.WhoisntCitizen_server.realtime.protocol.RealtimeMessageCodec;
import com.WhoisntCitizen_server.realtime.protocol.RealtimeProtocol;
import com.WhoisntCitizen_server.user.service.CurrentUserResolver;
import com.WhoisntCitizen_server.user.service.UserPresenceService;
import org.springframework.beans.factory.annotation.Qualifier;
import org.springframework.context.annotation.Bean;
import org.springframework.context.annotation.Configuration;
import org.springframework.scheduling.TaskScheduler;
import org.springframework.security.oauth2.jwt.JwtDecoder;
import org.springframework.web.socket.config.annotation.EnableWebSocket;
import org.springframework.web.socket.config.annotation.WebSocketConfigurer;
import org.springframework.web.socket.config.annotation.WebSocketHandlerRegistry;

import java.time.Clock;

/**
 * 실시간 알림 WebSocket(/ws/v1) 등록. 규칙은 Claude outputs/websocket-protocol.md.
 *
 * 구성
 *  - RealtimeWebSocketHandler: 연결·메시지 처리
 *  - RealtimeSessionRegistry: 이 서버의 연결 목록 (사용자 → 연결, 방 → 연결)
 *  - RealtimeNotifier: 다른 모듈이 신호를 보내는 입구. 지금은 LocalRealtimeNotifier(서버 1대), 5단계에서 Redis Pub/Sub 구현으로 바꾼다
 *  - RoomMembership: 구독할 때 방 참가자인지 확인 (로비 저장소)
 *  - RealtimePresenceRecorder: 연결 상태를 유저 접속 상태(온라인/대기실/게임 중)로 Redis에 기록
 *
 * Origin은 모두 허용한다. 인증을 쿠키가 아니라 JWT(헤더·쿼리)로 하므로 다른 사이트가 사용자 몰래 연결해도 토큰이 없어 열리지 않는다.
 * (Unity 데스크톱·모바일은 Origin 헤더를 보내지 않고, WebGL 빌드는 게임 서버와 다른 주소에서 열릴 수 있다)
 *
 * SecurityConfig에서 /ws/**는 permitAll(anyRequest)이다. 인증은 이 연결의 RealtimeHandshakeInterceptor가 한다.
 * 단, 잘못된 Authorization 헤더를 보내면 Spring Security(리소스 서버)가 먼저 401로 거절한다. 결과는 같다.
 */
@Configuration
@EnableWebSocket
public class RealtimeWebSocketConfig implements WebSocketConfigurer {

    private final JwtDecoder jwtDecoder;
    private final Clock clock;
    private final LobbyRoomRepository roomRepository;
    private final PlayerActivityTracker activityTracker;
    private final TaskScheduler scheduler;
    private final UserPresenceService userPresenceService;
    private final CurrentUserResolver currentUserResolver;

    public RealtimeWebSocketConfig(JwtDecoder jwtDecoder, Clock clock, LobbyRoomRepository roomRepository,
                                   PlayerActivityTracker activityTracker,
                                   @Qualifier("gamePhaseScheduler") TaskScheduler scheduler,
                                   UserPresenceService userPresenceService, CurrentUserResolver currentUserResolver) {
        this.jwtDecoder = jwtDecoder;
        this.clock = clock;
        this.roomRepository = roomRepository;
        this.activityTracker = activityTracker;
        this.scheduler = scheduler;
        this.userPresenceService = userPresenceService;
        this.currentUserResolver = currentUserResolver;
    }

    @Bean
    public RealtimeMessageCodec realtimeMessageCodec() {
        return new RealtimeMessageCodec();
    }

    @Bean
    public RealtimeSessionRegistry realtimeSessionRegistry() {
        return new RealtimeSessionRegistry();
    }

    @Bean
    public RealtimeNotifier realtimeNotifier() {
        return new LocalRealtimeNotifier(realtimeSessionRegistry(), realtimeMessageCodec());
    }

    /** 게임 상태가 저장되면 그 방에 game.changed를 보낸다 (4-4) */
    @Bean
    public RealtimeGameListener realtimeGameListener() {
        return new RealtimeGameListener(realtimeNotifier());
    }

    /** 방이 바뀌면 room.changed를 보내고, 방에서 빠진 사람·삭제된 방의 구독을 푼다 (4-5) */
    @Bean
    public RealtimeRoomListener realtimeRoomListener() {
        return new RealtimeRoomListener(realtimeNotifier());
    }

    /** 채팅 메시지가 저장되면 볼 수 있는 사람에게 chat.changed를 보낸다 (4-6) */
    @Bean
    public RealtimeChatListener realtimeChatListener() {
        return new RealtimeChatListener(realtimeNotifier());
    }

    @Bean
    public RoomMembership roomMembership() {
        return new LobbyRoomMembership(roomRepository);
    }

    /** 30초 동안 메시지가 없거나 토큰이 만료된 연결을 닫는다 (4-7) */
    @Bean
    public RealtimeConnectionMonitor realtimeConnectionMonitor() {
        return new RealtimeConnectionMonitor(scheduler, clock);
    }

    /** ping을 받으면 접속 기록(연결 끊김 판정)에 남긴다 (4-7) */
    @Bean
    public RealtimeActivityRecorder realtimeActivityRecorder() {
        return new GameActivityRecorder(roomRepository, activityTracker, clock);
    }

    /** 연결·ping·방 구독으로 유저 접속 상태(온라인/대기실/게임 중)를 Redis에 기록한다. 친구 목록이 읽는다 */
    @Bean
    public RealtimePresenceRecorder realtimePresenceRecorder() {
        return new UserPresenceRecorder(userPresenceService, currentUserResolver, roomRepository);
    }

    @Bean
    public RealtimeWebSocketHandler realtimeWebSocketHandler() {
        return new RealtimeWebSocketHandler(realtimeMessageCodec(), clock, realtimeSessionRegistry(), roomMembership(),
                realtimeConnectionMonitor(), realtimeActivityRecorder(), realtimePresenceRecorder());
    }

    @Override
    public void registerWebSocketHandlers(WebSocketHandlerRegistry registry) {
        registry.addHandler(realtimeWebSocketHandler(), RealtimeProtocol.PATH)
                .addInterceptors(new RealtimeHandshakeInterceptor(jwtDecoder))
                .setAllowedOriginPatterns("*");
    }
}
