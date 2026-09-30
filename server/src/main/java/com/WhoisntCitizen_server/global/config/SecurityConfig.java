package com.WhoisntCitizen_server.global.config;

import org.springframework.context.annotation.Bean;
import org.springframework.context.annotation.Configuration;
import org.springframework.security.config.annotation.web.builders.HttpSecurity;
import org.springframework.security.config.annotation.web.configurers.AbstractHttpConfigurer;
import org.springframework.security.crypto.bcrypt.BCryptPasswordEncoder;
import org.springframework.security.crypto.password.PasswordEncoder;
import org.springframework.security.web.SecurityFilterChain;

@Configuration
public class SecurityConfig {

	@Bean
	public PasswordEncoder passwordEncoder() {
		return new BCryptPasswordEncoder();
	}

	/**
	 * JWT 인증 적용 전 임시 설정: 모든 요청을 허용한다.
	 * 이 Bean이 없으면 Spring Security 기본값(모든 요청 인증 필요)이 적용되어 Postman 요청이 401이 된다.
	 * JWT 필터를 붙일 때 authorizeHttpRequests 규칙을 수정한다.
	 */
	@Bean
	public SecurityFilterChain securityFilterChain(HttpSecurity http) throws Exception {
		http
				.csrf(AbstractHttpConfigurer::disable)
				.httpBasic(AbstractHttpConfigurer::disable)
				.formLogin(AbstractHttpConfigurer::disable)
				.authorizeHttpRequests(auth -> auth.anyRequest().permitAll());
		return http.build();
	}
}
