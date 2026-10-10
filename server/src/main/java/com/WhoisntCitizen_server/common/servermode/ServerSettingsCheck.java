package com.WhoisntCitizen_server.common.servermode;

import lombok.extern.slf4j.Slf4j;
import org.springframework.boot.context.event.ApplicationReadyEvent;
import org.springframework.context.event.EventListener;
import org.springframework.core.env.Environment;
import org.springframework.stereotype.Component;

import java.util.ArrayList;
import java.util.List;
import java.util.StringJoiner;

/**
 * 서버가 켜질 때 실제로 쓰는 설정 조합을 로그로 남기고, 서로 안 맞는 조합이면 경고한다.
 * 경고만 하고 서버를 막지는 않는다. (서버 1대에서는 섞어 써도 동작하므로, 확인용으로 섞는 것은 허용한다)
 *
 * 경고 규칙 (warnings())
 *  1) multi 모드인데 일부 항목을 서버 메모리 방식으로 덮어썼다 → 서버끼리 그 상태를 나누지 못한다
 *  2) single 모드인데 일부만 redis로 덮어썼다 → 서버 1대에서는 괜찮지만 여러 대로 늘릴 준비가 된 것은 아니다
 *     (특히 게임 잠금만 redis이고 게임 저장소가 memory면 여러 대 준비가 된 것처럼 보여서 따로 짚는다)
 *  3) multi 모드 → 접속 기록(연결 끊김 판정)은 아직 서버 메모리라 서버는 1대만 띄워야 한다 (3.5단계에서 없앤다)
 */
@Slf4j
@Component
public class ServerSettingsCheck {

    private final Environment environment;

    public ServerSettingsCheck(Environment environment) {
        this.environment = environment;
    }

    @EventListener(ApplicationReadyEvent.class)
    public void report() {
        ServerSettings settings = ServerSettings.from(environment);
        log.info("서버 설정: {}", settings.summary());
        warnings(settings).forEach(log::warn);
    }

    static List<String> warnings(ServerSettings settings) {
        List<String> warnings = new ArrayList<>();
        List<ServerSetting> local = new ArrayList<>();
        List<ServerSetting> shared = new ArrayList<>();
        for (ServerSetting setting : ServerSetting.values()) {
            (setting.isShared(settings.get(setting)) ? shared : local).add(setting);
        }

        if (settings.mode() == ServerMode.MULTI) {
            if (!local.isEmpty()) {
                warnings.add("multi 모드인데 " + describe(settings, local) + "이(가) 서버 메모리 방식입니다. "
                        + "이 상태로 서버를 여러 대 띄우면 서버끼리 이 상태를 나누지 못합니다. 개별 설정을 비워 모드를 따르게 하세요.");
            }
            // 3.5단계(접속 기록)에서 설정이 생기면 이 경고는 그 설정이 local일 때만 내도록 바꾼다
            warnings.add("multi 모드에 필요한 접속 기록(연결 끊김 판정)이 아직 서버 메모리(local)입니다. 서버는 1대만 띄우세요.");
        } else if (!shared.isEmpty() && !local.isEmpty()) {
            warnings.add("single 모드에서 " + describe(settings, shared) + "만 redis입니다 (나머지: " + describe(settings, local) + "). "
                    + "서버 1대에서는 문제없지만, 서버를 여러 대로 늘릴 때는 " + ServerMode.PROPERTY + "=multi로 바꾸세요.");
            if (settings.get(ServerSetting.GAME_LOCK).equals(ServerSetting.GAME_LOCK.sharedValue())
                    && settings.get(ServerSetting.GAME_REPOSITORY).equals(ServerSetting.GAME_REPOSITORY.localValue())) {
                warnings.add("게임 잠금은 redis인데 게임 저장소는 memory입니다. 서버를 여러 대로 띄우면 게임이 공유되지 않습니다.");
            }
        }
        return warnings;
    }

    private static String describe(ServerSettings settings, List<ServerSetting> items) {
        StringJoiner joiner = new StringJoiner(", ");
        items.forEach(setting -> joiner.add(setting.label() + "=" + settings.get(setting)));
        return joiner.toString();
    }
}
