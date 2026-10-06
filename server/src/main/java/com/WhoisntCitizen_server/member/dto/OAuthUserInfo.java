package com.WhoisntCitizen_server.member.dto;

import com.WhoisntCitizen_server.member.entity.AuthProvider;
import lombok.Builder;
import lombok.Getter;

@Getter
@Builder
public class OAuthUserInfo {
	private String providerId;
	private AuthProvider provider;
	private String email;
	private String nickname;
}
