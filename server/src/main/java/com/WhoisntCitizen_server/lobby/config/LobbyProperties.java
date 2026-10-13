package com.WhoisntCitizen_server.lobby.config;

import org.springframework.boot.context.properties.ConfigurationProperties;
import org.springframework.boot.context.properties.bind.DefaultValue;

/**
 * application.properties 의 mafia.lobby.* 값 (초 단위).
 * 대기실 연결 끊김 처리(RoomPresenceService)에 쓴다.
 *
 * @param idleTimeoutSeconds  대기실에서 마지막 활동(방 조회·생성·입장) 후 이 시간이 지나면 방에서 뺀다.
 *                            0 이하면 연결 끊김 처리를 하지 않는다. (기록도 남기지 않음)
 *                            서버가 켜진 직후에는 이 시간만큼 기다렸다가 첫 검사를 한다. (재시작 직후 유예)
 * @param idleCheckSeconds    검사 주기. 실제로는 활동이 끊긴 지 idleTimeout ~ idleTimeout + idleCheck 사이에 빠진다.
 * @param presenceTtlSeconds  Redis 접속 기록 키(lobby:v1:presence:{roomId})의 TTL.
 *                            기록할 때마다 다시 연장되므로 사람이 있는 방의 키는 만료되지 않는다.
 *                            방 삭제 때 키를 지우지 못한 경우(서버가 죽는 등)를 대비한 안전장치다.
 */
@ConfigurationProperties(prefix = "mafia.lobby")
public record LobbyProperties(
        @DefaultValue("30") int idleTimeoutSeconds,
        @DefaultValue("5") int idleCheckSeconds,
        @DefaultValue("3600") int presenceTtlSeconds
) {

    /** 연결 끊김 처리를 하는지. 끄면 기록도, 검사도 하지 않는다. */
    public boolean idleCheckEnabled() {
        return idleTimeoutSeconds > 0 && idleCheckSeconds > 0;
    }
}
