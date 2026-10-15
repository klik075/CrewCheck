package com.WhoisntCitizen_server.global.config;

import org.springframework.context.annotation.Configuration;
import org.springframework.web.servlet.config.annotation.CorsRegistry;
import org.springframework.web.servlet.config.annotation.WebMvcConfigurer;

/**
 * Unity 에디터/PC 빌드는 CORS와 상관없지만,
 * WebGL 빌드(브라우저)에서 호출할 때를 대비해 /api/** 에 CORS를 허용합니다. (ChatPrac에서 가져옴)
 *
 * 이 설정은 Spring Security가 보안 필터 맨 앞에서 적용한다. (SecurityConfig의 .cors(Customizer.withDefaults()))
 * 그래서 토큰 없는 사전 요청(OPTIONS)도 인증 검사 전에 통과하고, 401·403 응답에도 CORS 헤더가 붙는다.
 *  - allowedOriginPatterns("*"): 쿠키가 아니라 JWT(Authorization 헤더)로 인증하므로 출처를 막을 이유가 없다
 *  - allowedMethods: 클라이언트가 쓰는 메서드 전부 (PATCH: 프로필 이미지 변경)
 *  - exposedHeaders("Retry-After"): 503 LOCK_BUSY 응답의 재시도 간격을 브라우저 코드가 읽을 수 있게
 *  - maxAge(3600): 사전 요청 결과를 브라우저가 1시간 기억 → 같은 요청마다 OPTIONS를 다시 보내지 않는다
 */
@Configuration
public class WebConfig implements WebMvcConfigurer {

    @Override
    public void addCorsMappings(CorsRegistry registry) {
        registry.addMapping("/api/**")
                .allowedOriginPatterns("*")
                .allowedMethods("GET", "POST", "PUT", "PATCH", "DELETE", "OPTIONS")
                .allowedHeaders("*")
                .exposedHeaders("Retry-After")
                .maxAge(3600);
    }
}
