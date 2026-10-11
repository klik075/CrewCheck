package com.WhoisntCitizen_server.lobby.config;

import org.springframework.boot.context.properties.EnableConfigurationProperties;
import org.springframework.context.annotation.Configuration;

/** 로비 설정값(mafia.lobby.*)을 LobbyProperties로 읽어 Bean으로 등록한다. */
@Configuration
@EnableConfigurationProperties(LobbyProperties.class)
public class LobbyConfig {
}
