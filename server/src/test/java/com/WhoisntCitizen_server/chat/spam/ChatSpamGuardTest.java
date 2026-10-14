package com.WhoisntCitizen_server.chat.spam;

import org.junit.jupiter.api.BeforeEach;
import org.junit.jupiter.api.Test;

import java.time.Clock;
import java.time.Duration;
import java.time.Instant;
import java.time.ZoneId;
import java.time.ZoneOffset;

import static org.assertj.core.api.Assertions.assertThat;
import static org.assertj.core.api.Assertions.assertThatThrownBy;
import static org.assertj.core.api.Assertions.catchThrowableOfType;

/** 도배 방지 규칙: 3번째부터 경고, 10번째에 3분 채팅 금지, 금지 중에는 모두 막음 */
class ChatSpamGuardTest {

    static final long USER = 1L, OTHER = 2L;

    /** 시간을 직접 움직이는 시계 */
    static final class TestClock extends Clock {
        Instant now = Instant.parse("2026-10-14T00:00:00Z");

        void advance(Duration d) {
            now = now.plus(d);
        }

        @Override public ZoneId getZone() { return ZoneOffset.UTC; }
        @Override public Clock withZone(ZoneId zone) { return this; }
        @Override public Instant instant() { return now; }
    }

    TestClock clock;
    ChatSpamGuard guard;

    @BeforeEach
    void setUp() {
        clock = new TestClock();
        guard = ChatSpamGuard.of(new InMemoryChatSpamStore(clock), ChatSpamPolicy.DEFAULT);
    }

    @Test
    void 두_번까지는_경고하지_않는다() {
        assertThat(guard.check(USER, "2번 마피아")).isNull();
        assertThat(guard.check(USER, "2번 마피아")).isNull();
    }

    @Test
    void 세_번째부터_아홉_번째까지_보낼_때마다_경고한다() {
        guard.check(USER, "도배");
        guard.check(USER, "도배");
        for (int n = 3; n <= 9; n++) {
            assertThat(guard.check(USER, "도배"))
                    .contains("같은 내용을 " + n + "번 연속으로 보냈습니다.")
                    .contains("10번째에 3분간 채팅이 금지됩니다.")
                    .endsWith("(" + n + "/10)");
        }
    }

    @Test
    void 열_번째에는_경고와_함께_3분간_채팅을_금지한다() {
        for (int i = 0; i < 9; i++) guard.check(USER, "도배");

        ChatMutedException e = catchThrowableOfType(ChatMutedException.class, () -> guard.check(USER, "도배"));
        assertThat(e.isJustMuted()).isTrue();
        assertThat(e.getWarning()).isEqualTo("같은 내용을 10번 연속으로 보냈습니다. (10/10)");
        assertThat(e.getMessage()).isEqualTo("도배로 3분간 채팅이 금지되었습니다.");
        assertThat(e.getRemainingSeconds()).isEqualTo(180);
    }

    @Test
    void 금지_중에는_다른_내용도_막고_남은_시간을_알려준다() {
        for (int i = 0; i < 10; i++) {
            try { guard.check(USER, "도배"); } catch (ChatMutedException ignored) { }
        }
        clock.advance(Duration.ofSeconds(45));

        ChatMutedException e = catchThrowableOfType(ChatMutedException.class, () -> guard.check(USER, "안녕하세요"));
        assertThat(e.isJustMuted()).isFalse();
        assertThat(e.getWarning()).isNull();
        assertThat(e.getRemainingSeconds()).isEqualTo(135);
        assertThat(e.getMessage()).isEqualTo("도배로 채팅이 금지된 상태입니다. 2분 15초 후에 다시 채팅할 수 있습니다.");

        // 다른 사람은 영향 없음
        assertThat(guard.check(OTHER, "도배")).isNull();
    }

    @Test
    void 금지가_끝나면_다시_1부터_센다() {
        for (int i = 0; i < 10; i++) {
            try { guard.check(USER, "도배"); } catch (ChatMutedException ignored) { }
        }
        clock.advance(Duration.ofMinutes(3));

        assertThat(guard.check(USER, "도배")).isNull();
        assertThat(guard.check(USER, "도배")).isNull();
        assertThat(guard.check(USER, "도배")).endsWith("(3/10)");
    }

    @Test
    void 다른_내용을_보내면_횟수가_처음부터_다시_시작된다() {
        guard.check(USER, "도배");
        guard.check(USER, "도배");
        assertThat(guard.check(USER, "도배")).isNotNull();

        assertThat(guard.check(USER, "다른 말")).isNull();
        assertThat(guard.check(USER, "도배")).isNull();
        assertThat(guard.check(USER, "도배")).isNull();
    }

    @Test
    void 공백과_영문_대소문자는_무시하고_같은_내용으로_본다() {
        guard.check(USER, "ㅋㅋ ㅋ");
        guard.check(USER, "  ㅋㅋㅋ ");
        assertThat(guard.check(USER, "ㅋ ㅋ ㅋ")).endsWith("(3/10)");

        guard.check(OTHER, "GG");
        guard.check(OTHER, "gg");
        assertThat(guard.check(OTHER, "Gg")).endsWith("(3/10)");
    }

    @Test
    void 같은_내용_사이가_60초를_넘으면_다시_센다() {
        guard.check(USER, "도배");
        clock.advance(Duration.ofSeconds(60));
        guard.check(USER, "도배"); // 60초까지는 이어서 센다 (2번째)
        clock.advance(Duration.ofSeconds(61));
        assertThat(guard.check(USER, "도배")).isNull(); // 1번째
    }

    @Test
    void 꺼진_가드는_아무것도_막지_않는다() {
        ChatSpamGuard off = ChatSpamGuard.disabled();
        for (int i = 0; i < 20; i++) assertThat(off.check(USER, "도배")).isNull();
    }

    @Test
    void 남은_시간_문구() {
        assertThat(ChatSpamGuard.formatDuration(180)).isEqualTo("3분");
        assertThat(ChatSpamGuard.formatDuration(125)).isEqualTo("2분 5초");
        assertThat(ChatSpamGuard.formatDuration(40)).isEqualTo("40초");
    }

    @Test
    void 잘못된_설정은_거부한다() {
        assertThatThrownBy(() -> new ChatSpamPolicy(3, 3, Duration.ofMinutes(3), Duration.ofSeconds(60)))
                .isInstanceOf(IllegalArgumentException.class);
    }
}
