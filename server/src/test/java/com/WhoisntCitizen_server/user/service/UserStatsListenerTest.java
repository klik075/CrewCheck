package com.WhoisntCitizen_server.user.service;

import com.WhoisntCitizen_server.game.entity.GameEndReason;
import com.WhoisntCitizen_server.game.entity.Winner;
import com.WhoisntCitizen_server.game.event.GameEndedEvent;
import com.WhoisntCitizen_server.jobs.domain.Faction;
import org.junit.jupiter.api.Test;

import java.util.List;

import static org.mockito.ArgumentMatchers.any;
import static org.mockito.Mockito.mock;
import static org.mockito.Mockito.never;
import static org.mockito.Mockito.verify;

/** 9. 전적 저장: 취소된 게임은 반영하지 않는다. */
class UserStatsListenerTest {

    private static final List<GameEndedEvent.PlayerOutcome> OUTCOMES = List.of(
            new GameEndedEvent.PlayerOutcome(1L, "PIRATE_RAIDER", Faction.PIRATE, true, false),
            new GameEndedEvent.PlayerOutcome(2L, "CREW_SAILOR", Faction.CREW, true, false));

    @Test
    void 취소된_게임은_전적에_반영하지_않는다() {
        UserStatsService stats = mock(UserStatsService.class);
        UserStatsListener listener = new UserStatsListener(stats, event -> { });

        listener.onGameEnded(new GameEndedEvent("g-1", "1", null, GameEndReason.CANCELLED_NO_DEATHS, 10, true, OUTCOMES));

        verify(stats, never()).recordGameResult(any());
    }

    @Test
    void 승리_팀이_정해진_게임은_전적에_반영한다() {
        UserStatsService stats = mock(UserStatsService.class);
        UserStatsListener listener = new UserStatsListener(stats, event -> { });
        GameEndedEvent event = new GameEndedEvent("g-1", "1", Winner.CREW, GameEndReason.WIN, 3, true, OUTCOMES);

        listener.onGameEnded(event);

        verify(stats).recordGameResult(event);
    }
}
