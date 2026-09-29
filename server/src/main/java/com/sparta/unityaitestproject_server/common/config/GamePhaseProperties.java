package com.sparta.unityaitestproject_server.common.config;

import org.springframework.boot.context.properties.ConfigurationProperties;
import org.springframework.boot.context.properties.bind.DefaultValue;

/** application.yml 의 mafia.phase.* 값 (초 단위). */
@ConfigurationProperties(prefix = "mafia.phase")
public record GamePhaseProperties(
        @DefaultValue("30") int nightSeconds,
        @DefaultValue("5") int nightResultSeconds,
        @DefaultValue("60") int daySeconds,
        @DefaultValue("30") int voteSeconds,
        @DefaultValue("5") int executionSeconds
) {
}
