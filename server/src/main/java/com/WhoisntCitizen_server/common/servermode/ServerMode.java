package com.WhoisntCitizen_server.common.servermode;

import java.util.Locale;

/**
 * 서버를 몇 대로 띄울지에 맞춘 묶음 설정. mafia.server.mode (환경변수 SERVER_MODE)
 *
 *  - SINGLE: 서버 1대. 게임 저장소·잠금을 전부 서버 메모리(memory/local)로 쓴다. Redis 없이도 게임이 돌아간다.
 *  - MULTI : 서버 여러 대. 게임 저장소·잠금을 전부 Redis로 쓴다. 서버끼리 같은 게임·같은 방을 함께 본다.
 *
 * 개별 설정(mafia.game.repository 등)은 비워 두면 모드를 따르고, 값을 넣으면 그 항목만 덮어쓴다. (ServerSetting 참고)
 * 값이 없거나 비어 있으면 SINGLE이다.
 */
public enum ServerMode {
    SINGLE,
    MULTI;

    public static final String PROPERTY = "mafia.server.mode";

    /** 설정 문자열을 모드로 바꾼다. 대소문자는 가리지 않는다. 오타는 서버가 뜨기 전에 막는다. */
    public static ServerMode from(String raw) {
        if (raw == null || raw.isBlank()) {
            return SINGLE;
        }
        String value = raw.trim().toUpperCase(Locale.ROOT);
        for (ServerMode mode : values()) {
            if (mode.name().equals(value)) {
                return mode;
            }
        }
        throw new IllegalStateException(PROPERTY + " 값 '" + raw + "'을(를) 알 수 없습니다. single 또는 multi만 쓸 수 있습니다.");
    }

    public String value() {
        return name().toLowerCase(Locale.ROOT);
    }
}
