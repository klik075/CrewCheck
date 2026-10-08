package com.WhoisntCitizen_server.game.activity;

import org.junit.jupiter.api.Test;

import java.time.Instant;
import java.util.List;
import java.util.Map;

import static org.assertj.core.api.Assertions.assertThat;

class LocalPlayerActivityTrackerTest {

    private static final Instant NOW = Instant.parse("2026-10-07T12:00:00Z");

    private final LocalPlayerActivityTracker tracker = new LocalPlayerActivityTracker();

    @Test
    void 시작할_때_모두를_접속으로_기록하고_요청마다_갱신한다() {
        tracker.markAllSeen("g1", List.of(1L, 2L), NOW);
        tracker.touch("g1", 2L, NOW.plusSeconds(5));

        assertThat(tracker.lastSeen("g1")).isEqualTo(Map.of(1L, NOW, 2L, NOW.plusSeconds(5)));
    }

    @Test
    void 게임별로_따로_기록한다() {
        tracker.touch("g1", 1L, NOW);
        tracker.touch("g2", 1L, NOW.plusSeconds(10));

        assertThat(tracker.lastSeen("g1")).containsEntry(1L, NOW);
        assertThat(tracker.lastSeen("g2")).containsEntry(1L, NOW.plusSeconds(10));
    }

    @Test
    void 돌려준_값은_복사본이라_이후_기록에_바뀌지_않는다() {
        tracker.touch("g1", 1L, NOW);
        Map<Long, Instant> snapshot = tracker.lastSeen("g1");

        tracker.touch("g1", 1L, NOW.plusSeconds(30));

        assertThat(snapshot).containsEntry(1L, NOW);
    }

    @Test
    void 지우면_기록이_없다() {
        tracker.touch("g1", 1L, NOW);
        tracker.clear("g1");

        assertThat(tracker.lastSeen("g1")).isEmpty();
        assertThat(tracker.lastSeen("unknown")).isEmpty();
    }

    @Test
    void playerId가_없으면_기록하지_않는다() {
        tracker.touch("g1", null, NOW);

        assertThat(tracker.lastSeen("g1")).isEmpty();
    }
}
