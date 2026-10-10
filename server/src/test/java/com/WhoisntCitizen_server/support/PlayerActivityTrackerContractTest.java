package com.WhoisntCitizen_server.support;

import com.WhoisntCitizen_server.game.activity.PlayerActivityTracker;
import org.junit.jupiter.api.BeforeEach;
import org.junit.jupiter.api.Test;

import java.time.Instant;
import java.util.List;
import java.util.Map;

import static org.assertj.core.api.Assertions.assertThat;

/**
 * 접속 기록 구현이라면 모두 지켜야 하는 동작 (PlayerActivityTracker 계약).
 * 구현마다 이 클래스를 상속해 newTracker()만 채운다.
 *  - LocalPlayerActivityTrackerTest (서버 메모리)
 *  - RedisPlayerActivityTrackerTest (3.5-2, Redis Hash)
 *
 * 각 테스트는 새 tracker로 시작한다. (Redis 구현은 newTracker()에서 기존 키를 비운다)
 * 시각은 밀리초 단위로만 쓴다. Redis 구현은 epoch 밀리초로 저장하기 때문이다.
 */
public abstract class PlayerActivityTrackerContractTest {

    protected static final Instant NOW = Instant.parse("2026-10-07T12:00:00Z");

    protected PlayerActivityTracker tracker;

    protected abstract PlayerActivityTracker newTracker();

    @BeforeEach
    void setUpTracker() {
        tracker = newTracker();
    }

    // 1. 기록·조회

    @Test
    void 기록한_시각을_그대로_돌려준다() {
        tracker.touch("g1", 1L, NOW);
        tracker.touch("g1", 2L, NOW.plusMillis(1500));

        assertThat(tracker.lastSeen("g1")).isEqualTo(Map.of(1L, NOW, 2L, NOW.plusMillis(1500)));
    }

    @Test
    void 같은_플레이어가_다시_요청하면_더_늦은_시각으로_바뀐다() {
        tracker.touch("g1", 1L, NOW);
        tracker.touch("g1", 1L, NOW.plusSeconds(5));

        assertThat(tracker.lastSeen("g1")).isEqualTo(Map.of(1L, NOW.plusSeconds(5)));
    }

    @Test
    void 기록이_없는_게임은_빈_Map이다() {
        assertThat(tracker.lastSeen("unknown")).isEmpty();
    }

    // 2. 시작 시 전원 기록

    @Test
    void 시작할_때_참가자_모두를_같은_시각으로_기록한다() {
        tracker.markAllSeen("g1", List.of(1L, 2L, 3L), NOW);

        assertThat(tracker.lastSeen("g1")).isEqualTo(Map.of(1L, NOW, 2L, NOW, 3L, NOW));
    }

    @Test
    void 시작_기록_뒤의_요청은_그_플레이어만_갱신한다() {
        tracker.markAllSeen("g1", List.of(1L, 2L), NOW);
        tracker.touch("g1", 2L, NOW.plusSeconds(5));

        assertThat(tracker.lastSeen("g1")).isEqualTo(Map.of(1L, NOW, 2L, NOW.plusSeconds(5)));
    }

    @Test
    void 참가자가_없으면_아무것도_기록하지_않는다() {
        tracker.markAllSeen("g1", List.of(), NOW);

        assertThat(tracker.lastSeen("g1")).isEmpty();
    }

    // 3. 더 늦은 시각만 반영

    @Test
    void 이른_시각의_요청은_기록을_되돌리지_않는다() {
        tracker.touch("g1", 1L, NOW.plusSeconds(10));
        tracker.touch("g1", 1L, NOW);   // 먼저 보냈지만 늦게 도착한 요청

        assertThat(tracker.lastSeen("g1")).containsEntry(1L, NOW.plusSeconds(10));
    }

    @Test
    void 시작_기록도_더_늦은_기록을_되돌리지_않는다() {
        tracker.touch("g1", 1L, NOW.plusSeconds(10));
        tracker.markAllSeen("g1", List.of(1L, 2L), NOW);

        assertThat(tracker.lastSeen("g1")).isEqualTo(Map.of(1L, NOW.plusSeconds(10), 2L, NOW));
    }

    @Test
    void 같은_시각으로_다시_기록해도_그대로다() {
        tracker.touch("g1", 1L, NOW);
        tracker.touch("g1", 1L, NOW);

        assertThat(tracker.lastSeen("g1")).isEqualTo(Map.of(1L, NOW));
    }

    // 4. 게임끼리 분리

    @Test
    void 게임별로_따로_기록한다() {
        tracker.touch("g1", 1L, NOW);
        tracker.touch("g2", 1L, NOW.plusSeconds(10));
        tracker.markAllSeen("g3", List.of(2L), NOW.plusSeconds(20));

        assertThat(tracker.lastSeen("g1")).isEqualTo(Map.of(1L, NOW));
        assertThat(tracker.lastSeen("g2")).isEqualTo(Map.of(1L, NOW.plusSeconds(10)));
        assertThat(tracker.lastSeen("g3")).isEqualTo(Map.of(2L, NOW.plusSeconds(20)));
    }

    // 5. 정리

    @Test
    void 지우면_그_게임의_기록만_사라진다() {
        tracker.touch("g1", 1L, NOW);
        tracker.touch("g2", 1L, NOW);

        tracker.clear("g1");

        assertThat(tracker.lastSeen("g1")).isEmpty();
        assertThat(tracker.lastSeen("g2")).isEqualTo(Map.of(1L, NOW));
    }

    @Test
    void 지운_뒤에는_새로_기록할_수_있다() {
        tracker.touch("g1", 1L, NOW.plusSeconds(10));
        tracker.clear("g1");

        tracker.touch("g1", 1L, NOW);   // 지웠으니 이전 기록과 비교하지 않는다

        assertThat(tracker.lastSeen("g1")).isEqualTo(Map.of(1L, NOW));
    }

    @Test
    void 없는_게임을_지워도_예외가_없다() {
        tracker.clear("unknown");

        assertThat(tracker.lastSeen("unknown")).isEmpty();
    }

    // 6. 복사본 반환

    @Test
    void 돌려준_값은_복사본이라_이후_기록에_바뀌지_않는다() {
        tracker.touch("g1", 1L, NOW);
        Map<Long, Instant> snapshot = tracker.lastSeen("g1");

        tracker.touch("g1", 1L, NOW.plusSeconds(30));
        tracker.touch("g1", 2L, NOW.plusSeconds(30));
        tracker.clear("g1");

        assertThat(snapshot).isEqualTo(Map.of(1L, NOW));
    }

    @Test
    void 돌려준_값을_고쳐도_기록은_그대로다() {
        tracker.touch("g1", 1L, NOW);
        Map<Long, Instant> snapshot = tracker.lastSeen("g1");

        try {
            snapshot.put(1L, NOW.plusSeconds(99));
            snapshot.put(2L, NOW);
        } catch (UnsupportedOperationException ignored) {
            // 고칠 수 없는 Map을 돌려줘도 된다
        }

        assertThat(tracker.lastSeen("g1")).isEqualTo(Map.of(1L, NOW));
    }

    // 7. 잘못된 입력

    @Test
    void gameId나_playerId가_없으면_기록하지_않는다() {
        tracker.touch("g1", null, NOW);
        tracker.touch(null, 1L, NOW);
        tracker.markAllSeen(null, List.of(1L), NOW);

        assertThat(tracker.lastSeen("g1")).isEmpty();
    }
}
