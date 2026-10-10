package com.WhoisntCitizen_server.common.servermode;

import org.junit.jupiter.api.Test;
import org.springframework.mock.env.MockEnvironment;

import static org.assertj.core.api.Assertions.assertThat;
import static org.assertj.core.api.Assertions.assertThatThrownBy;

/**
 * 모드와 개별 설정에서 "최종 값"을 정하는 규칙을 확인한다. (Spring 컨텍스트 없이)
 *  - 개별 설정이 비어 있으면 모드를 따른다 (single → memory/local, multi → redis)
 *  - 개별 설정에 값이 있으면 그 항목만 덮어쓴다
 *  - 모르는 값(오타)은 서버가 뜨기 전에 막는다
 */
class ServerSettingsTest {

    @Test
    void 아무것도_없으면_single이고_전부_서버_메모리다() {
        ServerSettings settings = ServerSettings.from(new MockEnvironment());

        assertThat(settings.mode()).isEqualTo(ServerMode.SINGLE);
        assertThat(settings.get(ServerSetting.GAME_REPOSITORY)).isEqualTo("memory");
        assertThat(settings.get(ServerSetting.GAME_LOCK)).isEqualTo("local");
        assertThat(settings.get(ServerSetting.ROOM_LOCK)).isEqualTo("local");
        assertThat(settings.get(ServerSetting.GAME_TIMER)).isEqualTo("local");
        assertThat(settings.overridden()).isEmpty();
    }

    @Test
    void multi면_전부_redis다() {
        ServerSettings settings = ServerSettings.from(env("mafia.server.mode", "multi"));

        assertThat(settings.mode()).isEqualTo(ServerMode.MULTI);
        assertThat(settings.values().values()).containsOnly("redis");
    }

    @Test
    void 개별_설정이_빈_문자열이면_모드를_따른다() {
        // application.properties의 ${GAME_LOCK:} 처럼 환경변수가 없으면 빈 문자열이 들어온다
        MockEnvironment env = env("mafia.server.mode", "multi")
                .withProperty("mafia.game.repository", "")
                .withProperty("mafia.game.lock", "  ");

        ServerSettings settings = ServerSettings.from(env);

        assertThat(settings.get(ServerSetting.GAME_REPOSITORY)).isEqualTo("redis");
        assertThat(settings.get(ServerSetting.GAME_LOCK)).isEqualTo("redis");
        assertThat(settings.overridden()).isEmpty();
    }

    @Test
    void 개별_설정에_값이_있으면_그_항목만_덮어쓴다() {
        MockEnvironment env = env("mafia.server.mode", "single").withProperty("mafia.game.lock", "redis");

        ServerSettings settings = ServerSettings.from(env);

        assertThat(settings.get(ServerSetting.GAME_LOCK)).isEqualTo("redis");
        assertThat(settings.get(ServerSetting.GAME_REPOSITORY)).isEqualTo("memory");
        assertThat(settings.get(ServerSetting.ROOM_LOCK)).isEqualTo("local");
        assertThat(settings.overridden()).containsExactly(ServerSetting.GAME_LOCK);
        assertThat(settings.summary()).isEqualTo("mode=single | 게임 저장소=memory | 게임 잠금=redis(직접 지정) | 방 잠금=local | 게임 타이머=local | 접속 기록=local");
    }

    @Test
    void 대소문자와_앞뒤_공백은_가리지_않는다() {
        MockEnvironment env = env("mafia.server.mode", " MULTI ").withProperty("mafia.room.lock", " Local ");

        ServerSettings settings = ServerSettings.from(env);

        assertThat(settings.mode()).isEqualTo(ServerMode.MULTI);
        assertThat(settings.get(ServerSetting.ROOM_LOCK)).isEqualTo("local");
    }

    @Test
    void 모드_오타는_막는다() {
        assertThatThrownBy(() -> ServerSettings.from(env("mafia.server.mode", "multy")))
                .isInstanceOf(IllegalStateException.class)
                .hasMessageContaining("mafia.server.mode")
                .hasMessageContaining("multy");
    }

    @Test
    void 항목에_맞지_않는_값은_막는다() {
        // 저장소는 memory|redis, 잠금은 local|redis. 서로 바꿔 쓰면 오타로 본다
        assertThatThrownBy(() -> ServerSettings.from(env("mafia.game.repository", "local")))
                .isInstanceOf(IllegalStateException.class)
                .hasMessageContaining("mafia.game.repository")
                .hasMessageContaining("memory 또는 redis");
        assertThatThrownBy(() -> ServerSettings.from(env("mafia.game.lock", "memory")))
                .isInstanceOf(IllegalStateException.class)
                .hasMessageContaining("local 또는 redis");
    }

    private static MockEnvironment env(String key, String value) {
        return new MockEnvironment().withProperty(key, value);
    }
}
