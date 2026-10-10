package com.WhoisntCitizen_server.common.servermode;

import java.util.Locale;

/**
 * 모드(ServerMode)를 따르는 개별 설정 목록.
 * 각 항목은 "서버 메모리 값"(single 기본)과 "Redis 값"(multi 기본) 두 가지만 가진다.
 *
 * 실제로 쓰는 값은 ServerSettings.resolve()가 정한다.
 *  1) 개별 설정에 값이 있으면 그 값 (덮어쓰기)
 *  2) 비어 있으면 모드의 기본값 (single → 서버 메모리 값, multi → Redis 값)
 *
 * 3단계(Redis 타이머), 3.5단계(접속 기록)에서 설정이 생기면 여기에 항목을 추가한다.
 * 그러면 모드 하나로 함께 바뀌고, 서버가 켜질 때 조합 검사(ServerSettingsCheck)에도 자동으로 들어간다.
 */
public enum ServerSetting {
    GAME_REPOSITORY("mafia.game.repository", "게임 저장소", "memory", "redis"),
    GAME_LOCK("mafia.game.lock", "게임 잠금", "local", "redis"),
    ROOM_LOCK("mafia.room.lock", "방 잠금", "local", "redis");

    private final String key;
    private final String label;
    private final String localValue;
    private final String sharedValue;

    ServerSetting(String key, String label, String localValue, String sharedValue) {
        this.key = key;
        this.label = label;
        this.localValue = localValue;
        this.sharedValue = sharedValue;
    }

    /** 설정 이름 (예: mafia.game.lock) */
    public String key() {
        return key;
    }

    /** 로그에 쓰는 한국어 이름 (예: 게임 잠금) */
    public String label() {
        return label;
    }

    /** 서버 메모리 방식 값 (memory 또는 local). 서버 1대에서만 맞다. */
    public String localValue() {
        return localValue;
    }

    /** Redis 방식 값 (redis). 서버 여러 대가 함께 쓴다. */
    public String sharedValue() {
        return sharedValue;
    }

    /** 개별 설정이 비어 있을 때 쓰는 값 */
    public String defaultFor(ServerMode mode) {
        return mode == ServerMode.MULTI ? sharedValue : localValue;
    }

    public boolean isShared(String value) {
        return sharedValue.equals(value);
    }

    /** 앞뒤 공백·대소문자를 정리하고, 이 항목이 쓸 수 없는 값이면 서버가 뜨기 전에 막는다. */
    String normalize(String raw) {
        String value = raw.trim().toLowerCase(Locale.ROOT);
        if (!value.equals(localValue) && !value.equals(sharedValue)) {
            throw new IllegalStateException(key + " 값 '" + raw + "'을(를) 알 수 없습니다. "
                    + localValue + " 또는 " + sharedValue + "만 쓸 수 있습니다. (비워 두면 " + ServerMode.PROPERTY + "를 따릅니다)");
        }
        return value;
    }
}
