package com.WhoisntCitizen_server.global.config;

import org.springframework.context.annotation.Configuration;
import org.springframework.web.servlet.config.annotation.CorsRegistry;
import org.springframework.web.servlet.config.annotation.WebMvcConfigurer;

/**
 * Unity 에디터/PC 빌드는 CORS와 상관없지만,
 * WebGL 빌드(브라우저)에서 호출할 때를 대비해 /api/** 에 CORS를 허용합니다. (ChatPrac에서 가져옴)
 */
@Configuration
public class WebConfig implements WebMvcConfigurer {

    @Override
    public void addCorsMappings(CorsRegistry registry) {
        registry.addMapping("/api/**")
                .allowedOriginPatterns("*")
                .allowedMethods("GET", "POST", "PUT", "DELETE", "OPTIONS")
                .allowedHeaders("*");
    }
}
