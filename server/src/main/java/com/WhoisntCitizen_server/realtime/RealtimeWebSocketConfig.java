package com.WhoisntCitizen_server.realtime;

import com.WhoisntCitizen_server.common.servermode.ConditionalOnServerSetting;
import com.WhoisntCitizen_server.common.servermode.ServerSetting;
import com.WhoisntCitizen_server.game.activity.PlayerActivityTracker;
import com.WhoisntCitizen_server.lobby.presence.RoomPresenceService;
import com.WhoisntCitizen_server.lobby.repository.LobbyRoomRepository;
import com.WhoisntCitizen_server.realtime.protocol.RealtimeMessageCodec;
import com.WhoisntCitizen_server.realtime.protocol.RealtimeProtocol;
import com.WhoisntCitizen_server.user.service.CurrentUserResolver;
import com.WhoisntCitizen_server.user.service.UserPresenceService;
import org.springframework.beans.factory.ObjectProvider;
import org.springframework.beans.factory.annotation.Qualifier;
import org.springframework.beans.factory.annotation.Value;
import org.springframework.context.annotation.Bean;
import org.springframework.context.annotation.Configuration;
import org.springframework.scheduling.TaskScheduler;
import org.springframework.security.oauth2.jwt.JwtDecoder;
import org.springframework.web.socket.config.annotation.EnableWebSocket;
import org.springframework.web.socket.config.annotation.WebSocketConfigurer;
import org.springframework.web.socket.config.annotation.WebSocketHandlerRegistry;

import java.time.Clock;
import java.time.Duration;

/**
 * 실시간 알림 WebSocket(/ws/v1) 등록. 규칙은 Claude outputs/websocket-protocol.md.
 *
 * 구성
 *  - RealtimeWebSocketHandler: 연결·메시지 처리
 *  - RealtimeSessionRegistry: 이 서버의 연결 목록 (사용자 → 연결, 방 → 연결)
 *  - RealtimeNotifier: 다른 모듈이 신호를 보내는 입구. mafia.realtime.notifier의 최종 값으로 고른다
 *      local → LocalRealtimeNotifier (이 클래스, 서버 1대) / redis → RedisRealtimeNotifier (RealtimeRedisConfig, 서버 여러 대)
 *  - RoomMembership: 구독할 때 방 참가자인지 확인 (로비 저장소)
 *  - RealtimePresenceRecorder: 연결 상태를 유저 접속 상태(온라인/대기실/게임 중)로 Redis에 기록
 *
 * Origin은 모두 허용한다. 인증을 쿠키가 아니라 JWT(헤더·쿼리)로 하므로 다른 사이트가 사용자 몰래 연결해도 토큰이 없어 곧바로 4401로 닫힌다.
 * (Unity 데스크톱·모바일은 Origin 헤더를 보내지 않고, WebGL 빌드는 게임 서버와 다른 주소에서 열릴 수 있다)
 *
 * SecurityConfig에서 /ws/**는 permitAll(anyRequest)이고, Spring Security는 /ws/** 요청의 Authorization 헤더를 읽지 않는다
 * (SecurityConfig.bearerTokenResolver). 인증은 이 연결의 RealtimeHandshakeInterceptor가 하고, 실패하면 연결 직후 4401로 닫는다.
 * Security가 헤더를 읽으면 잘못된 토큰을 HTTP 401로 먼저 거절해 버려서, 클라이언트가 토큰 문제를 알 수 없게 된다.
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
    /** 대기실 접속 기록. 로비 서비스라 설정 클래스를 만들 때가 아니라 Bean을 만들 때 꺼낸다 (순환 참조 방지) */
    private final ObjectProvider<RoomPresenceService> roomPresenceService;
    private final Duration shutdownPresenceGrace;

    public RealtimeWebSocketConfig(JwtDecoder jwtDecoder, Clock clock, LobbyRoomRepository roomRepository,
                                   PlayerActivityTracker activityTracker,
                                   @Qualifier("gamePhaseScheduler") TaskScheduler scheduler,
                                   UserPresenceService userPresenceService, CurrentUserResolver currentUserResolver,
                                   ObjectProvider<RoomPresenceService> roomPresenceService,
                                   @Value("${mafia.realtime.shutdown-presence-grace-seconds:10}") long shutdownPresenceGraceSeconds) {
        this.jwtDecoder = jwtDecoder;
        this.clock = clock;
        this.roomRepository = roomRepository;
        this.activityTracker = activityTracker;
        this.scheduler = scheduler;
        this.userPresenceService = userPresenceService;
        this.currentUserResolver = currentUserResolver;
        this.roomPresenceService = roomPresenceService;
        this.shutdownPresenceGrace = Duration.ofSeconds(shutdownPresenceGraceSeconds);
    }

    @Bean
    public RealtimeMessageCodec realtimeMessageCodec() {
        return new RealtimeMessageCodec();
    }

    @Bean
    public RealtimeSessionRegistry realtimeSessionRegistry() {
        return new RealtimeSessionRegistry();
    }

    /**
     * 서버 메모리 실시간 신호. mafia.realtime.notifier의 최종 값이 local일 때만 등록한다. (서버 1대)
     * redis면 이 Bean은 만들지 않고 RealtimeRedisConfig의 RedisRealtimeNotifier가 RealtimeNotifier가 된다.
     */
    @Bean
    @ConditionalOnServerSetting(value = ServerSetting.REALTIME_NOTIFIER, havingValue = "local")
    public RealtimeNotifier realtimeNotifier() {
        return new LocalRealtimeNotifier(realtimeSessionRegistry(), realtimeMessageCodec());
    }

    // 아래 리스너들은 RealtimeNotifier를 메서드 호출(realtimeNotifier())이 아니라 인자로 받는다.
    // 어느 구현이 등록됐는지(local/redis)는 설정에 따라 달라지므로, Spring이 등록된 쪽을 넣어 주게 한다.

    /** 게임 상태가 저장되면 그 방에 game.changed를 보낸다 (4-4) */
    @Bean
    public RealtimeGameListener realtimeGameListener(RealtimeNotifier notifier) {
        return new RealtimeGameListener(notifier);
    }

    /** 방이 바뀌면 room.changed를 보내고, 방에서 빠진 사람·삭제된 방의 구독을 푼다 (4-5) */
    @Bean
    public RealtimeRoomListener realtimeRoomListener(RealtimeNotifier notifier) {
        return new RealtimeRoomListener(notifier);
    }

    /** 채팅 메시지가 저장되면 볼 수 있는 사람에게 chat.changed를 보낸다 (4-6) */
    @Bean
    public RealtimeChatListener realtimeChatListener(RealtimeNotifier notifier) {
        return new RealtimeChatListener(notifier);
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

    /** ping·방 구독을 접속 기록(연결 끊김 판정)에 남긴다. 게임 중이면 게임 기록, 대기 중이면 대기실 기록 (4-7, 멀티서버 3단계) */
    @Bean
    public RealtimeActivityRecorder realtimeActivityRecorder() {
        return new GameActivityRecorder(roomRepository, activityTracker, roomPresenceService.getObject(), clock);
    }

    /** 연결·ping·방 구독으로 유저 접속 상태(온라인/대기실/게임 중)를 Redis에 기록한다. 친구 목록이 읽는다 */
    @Bean
    public RealtimePresenceRecorder realtimePresenceRecorder() {
        return new UserPresenceRecorder(userPresenceService, currentUserResolver, roomRepository);
    }

    /** 서버가 꺼질 때 연결을 모두 1001로 닫아, 클라이언트가 곧바로 다른 서버로 다시 붙게 한다 (멀티서버 2단계) */
    @Bean
    public RealtimeShutdownCloser realtimeShutdownCloser() {
        return new RealtimeShutdownCloser(realtimeSessionRegistry());
    }

    @Bean
    public RealtimeWebSocketHandler realtimeWebSocketHandler() {
        return new RealtimeWebSocketHandler(realtimeMessageCodec(), clock, realtimeSessionRegistry(), roomMembership(),
                realtimeConnectionMonitor(), realtimeActivityRecorder(), realtimePresenceRecorder(),
                realtimeShutdownCloser()::isShuttingDown, shutdownPresenceGrace);
    }

    @Override
    public void registerWebSocketHandlers(WebSocketHandlerRegistry registry) {
        registry.addHandler(realtimeWebSocketHandler(), RealtimeProtocol.PATH)
                .addInterceptors(new RealtimeHandshakeInterceptor(jwtDecoder))
                .setAllowedOriginPatterns("*");
    }
}
