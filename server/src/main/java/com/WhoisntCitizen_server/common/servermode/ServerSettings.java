package com.WhoisntCitizen_server.common.servermode;

import org.springframework.core.env.PropertyResolver;

import java.util.Collections;
import java.util.EnumMap;
import java.util.EnumSet;
import java.util.Map;
import java.util.Set;
import java.util.StringJoiner;

/**
 * 서버가 실제로 쓰는 설정 조합. (모드 + 항목별 최종 값 + 직접 덮어쓴 항목)
 *
 * Bean을 고르는 조건(@ConditionalOnServerSetting)과 서버 시작 로그(ServerSettingsCheck)가
 * 같은 resolve()를 쓰므로, 로그에 찍힌 값과 실제로 등록된 구현이 어긋나지 않는다.
 */
public record ServerSettings(ServerMode mode, Map<ServerSetting, String> values, Set<ServerSetting> overridden) {

    public ServerSettings {
        Map<ServerSetting, String> copiedValues = new EnumMap<>(ServerSetting.class);
        copiedValues.putAll(values);
        Set<ServerSetting> copiedOverridden = EnumSet.noneOf(ServerSetting.class);
        copiedOverridden.addAll(overridden);
        values = Collections.unmodifiableMap(copiedValues);
        overridden = Collections.unmodifiableSet(copiedOverridden);
    }

    public static ServerMode mode(PropertyResolver env) {
        return ServerMode.from(env.getProperty(ServerMode.PROPERTY));
    }

    /** 개별 설정에 값이 있으면 그 값, 비어 있으면 모드의 기본값 */
    public static String resolve(PropertyResolver env, ServerSetting setting) {
        String raw = env.getProperty(setting.key());
        if (raw == null || raw.isBlank()) {
            return setting.defaultFor(mode(env));
        }
        return setting.normalize(raw);
    }

    public static ServerSettings from(PropertyResolver env) {
        Map<ServerSetting, String> values = new EnumMap<>(ServerSetting.class);
        Set<ServerSetting> overridden = EnumSet.noneOf(ServerSetting.class);
        for (ServerSetting setting : ServerSetting.values()) {
            values.put(setting, resolve(env, setting));
            String raw = env.getProperty(setting.key());
            if (raw != null && !raw.isBlank()) {
                overridden.add(setting);
            }
        }
        return new ServerSettings(mode(env), values, overridden);
    }

    public String get(ServerSetting setting) {
        return values.get(setting);
    }

    /** 예: "mode=single | 게임 저장소=memory | 게임 잠금=redis(직접 지정) | 방 잠금=local" */
    public String summary() {
        StringJoiner joiner = new StringJoiner(" | ");
        joiner.add("mode=" + mode.value());
        for (ServerSetting setting : ServerSetting.values()) {
            joiner.add(setting.label() + "=" + get(setting) + (overridden.contains(setting) ? "(직접 지정)" : ""));
        }
        return joiner.toString();
    }
}
