package com.WhoisntCitizen_server.global.config;

import com.WhoisntCitizen_server.global.util.JwtTokenProvider;
import org.springframework.context.annotation.Bean;
import org.springframework.context.annotation.Configuration;
import org.springframework.http.HttpStatus;
import org.springframework.security.config.Customizer;
import org.springframework.security.config.annotation.web.builders.HttpSecurity;
import org.springframework.security.config.http.SessionCreationPolicy;
import org.springframework.security.crypto.bcrypt.BCryptPasswordEncoder;
import org.springframework.security.crypto.password.PasswordEncoder;
import org.springframework.security.oauth2.jose.jws.MacAlgorithm;
import org.springframework.security.oauth2.jwt.JwtDecoder;
import org.springframework.security.oauth2.jwt.NimbusJwtDecoder;
import org.springframework.security.oauth2.server.resource.web.BearerTokenResolver;
import org.springframework.security.oauth2.server.resource.web.DefaultBearerTokenResolver;
import org.springframework.security.web.SecurityFilterChain;
import org.springframework.security.web.authentication.HttpStatusEntryPoint;
import org.springframework.security.config.annotation.web.configurers.AbstractHttpConfigurer;

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
	 *
	// @Bean
	// public SecurityFilterChain securityFilterChain(HttpSecurity http) throws Exception {
	// 	http
	// 			.csrf(AbstractHttpConfigurer::disable)
	// 			.httpBasic(AbstractHttpConfigurer::disable)
	// 			.formLogin(AbstractHttpConfigurer::disable)
	// 			.authorizeHttpRequests(auth -> auth.anyRequest().permitAll());
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
				// CORS를 보안 필터 맨 앞에서 처리한다. 설정 내용은 WebConfig.addCorsMappings를 그대로 쓴다.
				// 없으면 브라우저(WebGL)의 사전 요청(OPTIONS, 토큰 없음)이 아래 authenticated() 규칙에 걸려 401이 되고,
				// 401·403 응답에 CORS 헤더가 없어 브라우저가 오류 내용도 읽지 못한다. (Unity 에디터·PC 빌드는 CORS와 상관없다)
				.cors(Customizer.withDefaults())
				.csrf(csrf -> csrf.disable())
				.httpBasic(basic -> basic.disable())
				.formLogin(form -> form.disable())
				.sessionManagement(s -> s.sessionCreationPolicy(SessionCreationPolicy.STATELESS))
				.authorizeHttpRequests(auth -> auth
						// 회원가입 / 로그인은 토큰 없이
						.requestMatchers("/api/members/signup", "/api/members/login",
									"/api/members/login/google", "/api/members/login/kakao").permitAll()
						// 토큰 재발급 / 로그아웃은 body의 리프레시 토큰으로 인증한다
						.requestMatchers("/api/auth/**").permitAll()
						// 로비(/api/v1/rooms), 게임(/api/v1/games), 직업(/api/v1/roles) API는 로그인한 유저만
						.requestMatchers("/api/v1/**").authenticated()
						.anyRequest().permitAll())
				.oauth2ResourceServer(oauth2 -> oauth2
						// /ws/ 와 /api/auth/ 는 Authorization 헤더를 읽지 않는다 (아래 bearerTokenResolver 참고)
						.bearerTokenResolver(bearerTokenResolver())
						.jwt(jwt -> {}))
				.exceptionHandling(e -> e.authenticationEntryPoint(new HttpStatusEntryPoint(HttpStatus.UNAUTHORIZED)));

		return http.build();
	}

	/**
	 * 요청에서 Bearer 토큰을 꺼내는 규칙. null을 돌려주면 "토큰 없음"으로 보고 검사하지 않는다.
	 *
	 * 기본 규칙은 permitAll 경로라도 Authorization: Bearer 헤더가 있으면 검증하고, 틀리면 HTTP 401로 거절한다.
	 * 아래 두 경로는 그 규칙을 빼고, 나머지 경로(REST)는 지금과 같다.
	 *
	 *  - /ws/      : WebSocket 연결이 HTTP 401로 거절되면 Unity·브라우저 모두 "연결 실패"로만 알아서 토큰 문제인지 모른다.
	 *                그래서 Security가 건드리지 않고, RealtimeHandshakeInterceptor가 직접 확인해 실패하면 연결 후 4401로 닫는다.
	 *  - /api/auth/: 토큰 재발급·로그아웃은 body의 리프레시 토큰으로 인증한다.
	 *                만료된 액세스 토큰이 헤더에 남아 있어도 401로 막히지 않게 헤더를 읽지 않는다.
	 */
	private static BearerTokenResolver bearerTokenResolver() {
		DefaultBearerTokenResolver header = new DefaultBearerTokenResolver();
		return request -> {
			String uri = request.getRequestURI();
			if (uri.startsWith("/ws/") || uri.startsWith("/api/auth/")) {
				return null;
			}
			return header.resolve(request);
		};
	}
}
