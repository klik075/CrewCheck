package com.WhoisntCitizen_server.game.scheduling;

import org.junit.jupiter.api.Test;

import java.util.ArrayList;
import java.util.List;

import static org.assertj.core.api.Assertions.assertThat;
import static org.assertj.core.api.Assertions.assertThatThrownBy;

/** 3-2: 타이머 이름(TimerKeys)을 만들고 다시 읽으면 같은 대상이 되는지 확인한다. Redis에는 이 이름만 저장된다. */
class TimerKeysTest {

    @Test
    void 페이즈_타이머_이름을_만들고_다시_읽는다() {
        String key = TimerKeys.phase("abc", 3);

        assertThat(key).isEqualTo("phase:abc:3");
        assertThat(TimerKeys.parse(key)).isEqualTo(TimerTarget.phase("abc", 3));
        assertThat(TimerKeys.parse(key).key()).isEqualTo(key);
    }

    @Test
    void 정리_타이머_이름을_만들고_다시_읽는다() {
        String key = TimerKeys.cleanup("abc");

        assertThat(key).isEqualTo("cleanup:abc");
        assertThat(TimerKeys.parse(key)).isEqualTo(TimerTarget.cleanup("abc"));
        assertThat(TimerKeys.parse(key).key()).isEqualTo(key);
    }

    @Test
    void gameId에_콜론이_있어도_버전은_마지막_콜론_뒤에서_읽는다() {
        TimerTarget target = TimerKeys.parse(TimerKeys.phase("room:7:game", 12));

        assertThat(target.gameId()).isEqualTo("room:7:game");
        assertThat(target.phaseVersion()).isEqualTo(12);
    }

    @Test
    void 형식이_맞지_않는_이름은_거부한다() {
        for (String bad : List.of("", "phase:", "phase:abc", "phase:abc:", "phase::3", "phase:abc:x", "cleanup:", "timer:abc")) {
            assertThatThrownBy(() -> TimerKeys.parse(bad)).as(bad).isInstanceOf(IllegalArgumentException.class);
        }
        assertThatThrownBy(() -> TimerKeys.parse(null)).isInstanceOf(IllegalArgumentException.class);
    }

    @Test
    void 대상은_알맞은_handler_메서드를_부른다() {
        List<String> calls = new ArrayList<>();
        GameTimeoutHandler handler = new GameTimeoutHandler() {
            @Override
            public void onPhaseTimeout(String gameId, long phaseVersion) {
                calls.add("phase " + gameId + " v" + phaseVersion);
            }

            @Override
            public void onCleanup(String gameId) {
                calls.add("cleanup " + gameId);
            }
        };

        TimerTarget.phase("g1", 3).fire(handler);
        TimerTarget.cleanup("g1").fire(handler);

        assertThat(calls).containsExactly("phase g1 v3", "cleanup g1");
    }
}
