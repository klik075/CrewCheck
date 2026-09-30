package com.WhoisntCitizen_server.global.config;

import com.WhoisntCitizen_server.global.util.JwtTokenProvider;
import org.springframework.context.annotation.Bean;
import org.springframework.context.annotation.Configuration;
import org.springframework.http.HttpStatus;
import org.springframework.security.config.annotation.web.builders.HttpSecurity;
import org.springframework.security.config.http.SessionCreationPolicy;
import org.springframework.security.crypto.bcrypt.BCryptPasswordEncoder;
import org.springframework.security.crypto.password.PasswordEncoder;
import org.springframework.security.oauth2.jose.jws.MacAlgorithm;
import org.springframework.security.oauth2.jwt.JwtDecoder;
import org.springframework.security.oauth2.jwt.NimbusJwtDecoder;
import org.springframework.security.web.SecurityFilterChain;
import org.springframework.security.web.authentication.HttpStatusEntryPoint;

@Configuration
public class SecurityConfig {

	@Bean
	public PasswordEncoder passwordEncoder() {
		return new BCryptPasswordEncoder();
	}

	/**
	 * 로그인 시 JwtTokenProvider가 발급한 토큰을 같은 키(HS256)로 검증한다.
	 * 검증에 성공하면 컨트롤러에서 @AuthenticationPrincipal Jwt 로 받을 수 있고,
	 * jwt.getSubject() 가 memberId 이다. (jobs 패키지 RoleController와 같은 방식)
	 */
	@Bean
	public JwtDecoder jwtDecoder(JwtTokenProvider jwtTokenProvider) {
		return NimbusJwtDecoder.withSecretKey(jwtTokenProvider.getKey())
				.macAlgorithm(MacAlgorithm.HS256)
				.build();
	}

	@Bean
	public SecurityFilterChain securityFilterChain(HttpSecurity http) throws Exception {
		http
				.csrf(csrf -> csrf.disable())
				.httpBasic(basic -> basic.disable())
				.formLogin(form -> form.disable())
				.sessionManagement(s -> s.sessionCreationPolicy(SessionCreationPolicy.STATELESS))
				.authorizeHttpRequests(auth -> auth
						// 회원가입 / 로그인은 토큰 없이
						.requestMatchers("/api/members/signup", "/api/members/login").permitAll()
						// 로비(/api/v1/rooms)와 직업 API(/api/v1/...)는 로그인한 유저만
						.requestMatchers("/api/v1/**").authenticated()
						// TODO: game / vote 모듈(/api/games/**)도 토큰 기반으로 옮기면 authenticated()로 변경
						.anyRequest().permitAll())
				.oauth2ResourceServer(oauth2 -> oauth2.jwt(jwt -> {}))
				.exceptionHandling(e -> e.authenticationEntryPoint(new HttpStatusEntryPoint(HttpStatus.UNAUTHORIZED)));

		return http.build();
	}
}
