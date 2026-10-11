package com.WhoisntCitizen_server.realtime;

import com.WhoisntCitizen_server.realtime.protocol.RealtimeMessageCodec;
import com.WhoisntCitizen_server.realtime.protocol.RealtimeProtocol;
import org.springframework.context.annotation.Bean;
import org.springframework.context.annotation.Configuration;
import org.springframework.security.oauth2.jwt.JwtDecoder;
import org.springframework.web.socket.config.annotation.EnableWebSocket;
import org.springframework.web.socket.config.annotation.WebSocketConfigurer;
import org.springframework.web.socket.config.annotation.WebSocketHandlerRegistry;

import java.time.Clock;

/**
 * 실시간 알림 WebSocket(/ws/v1) 등록. 규칙은 Claude ouputs/websocket-protocol.md.
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

    public RealtimeWebSocketConfig(JwtDecoder jwtDecoder, Clock clock) {
        this.jwtDecoder = jwtDecoder;
        this.clock = clock;
    }

    @Bean
    public RealtimeMessageCodec realtimeMessageCodec() {
        return new RealtimeMessageCodec();
    }

    @Bean
    public RealtimeWebSocketHandler realtimeWebSocketHandler() {
        return new RealtimeWebSocketHandler(realtimeMessageCodec(), clock);
    }

    @Override
    public void registerWebSocketHandlers(WebSocketHandlerRegistry registry) {
        registry.addHandler(realtimeWebSocketHandler(), RealtimeProtocol.PATH)
                .addInterceptors(new RealtimeHandshakeInterceptor(jwtDecoder))
                .setAllowedOriginPatterns("*");
    }
}
