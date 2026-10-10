package com.WhoisntCitizen_server.common.servermode;

import org.junit.jupiter.api.Test;
import org.springframework.mock.env.MockEnvironment;

import java.util.List;

import static org.assertj.core.api.Assertions.assertThat;

/**
 * 서버가 켜질 때 내는 조합 경고를 확인한다. 경고만 하고 서버를 막지는 않는다.
 */
class ServerSettingsCheckTest {

    @Test
    void single에서_전부_서버_메모리면_경고가_없다() {
        assertThat(warnings()).isEmpty();
    }

    @Test
    void single에서_전부_redis로_덮어써도_섞이지_않았으니_경고가_없다() {
        assertThat(warnings("mafia.game.repository", "redis", "mafia.game.lock", "redis", "mafia.room.lock", "redis",
                "mafia.game.timer", "redis", "mafia.game.activity", "redis"))
                .isEmpty();
    }

    @Test
    void single에서_일부만_redis면_섞였다고_경고한다() {
        List<String> warnings = warnings("mafia.game.repository", "redis");

        assertThat(warnings).hasSize(1);
        assertThat(warnings.get(0)).contains("게임 저장소=redis", "게임 잠금=local", "방 잠금=local", "mafia.server.mode=multi");
    }

    @Test
    void 게임_잠금만_redis이고_저장소가_memory면_따로_짚는다() {
        List<String> warnings = warnings("mafia.game.lock", "redis");

        assertThat(warnings).hasSize(2);
        assertThat(warnings.get(1)).isEqualTo("게임 잠금은 redis인데 게임 저장소는 memory입니다. 서버를 여러 대로 띄우면 게임이 공유되지 않습니다.");
    }

    @Test
    void multi에서_전부_redis면_경고가_없다() {
        // 3.5단계 전에는 접속 기록이 서버 메모리뿐이라 multi면 항상 "서버는 1대만" 경고를 냈다
        assertThat(warnings("mafia.server.mode", "multi")).isEmpty();
    }

    @Test
    void multi에서_일부를_서버_메모리로_덮어쓰면_그_항목을_짚는다() {
        List<String> warnings = warnings("mafia.server.mode", "multi", "mafia.room.lock", "local");

        assertThat(warnings).hasSize(1);
        assertThat(warnings.get(0)).contains("multi 모드인데", "방 잠금=local").doesNotContain("게임 잠금");
    }

    @Test
    void multi에서_접속_기록을_서버_메모리로_덮어쓰면_짚는다() {
        List<String> warnings = warnings("mafia.server.mode", "multi", "mafia.game.activity", "local");

        assertThat(warnings).hasSize(1);
        assertThat(warnings.get(0)).contains("multi 모드인데", "접속 기록=local");
    }

    @Test
    void single에서_접속_기록만_redis면_섞였다고_경고한다() {
        List<String> warnings = warnings("mafia.game.activity", "redis");

        assertThat(warnings).hasSize(1);
        assertThat(warnings.get(0)).contains("접속 기록=redis", "게임 저장소=memory");
    }

    private static List<String> warnings(String... keyValues) {
        MockEnvironment env = new MockEnvironment();
        for (int i = 0; i < keyValues.length; i += 2) {
            env.setProperty(keyValues[i], keyValues[i + 1]);
        }
        return ServerSettingsCheck.warnings(ServerSettings.from(env));
    }
}
