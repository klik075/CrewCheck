package com.WhoisntCitizen_server.user.service;

import com.WhoisntCitizen_server.common.event.RoomNoticeEvent;
import com.WhoisntCitizen_server.game.entity.GameEndReason;
import com.WhoisntCitizen_server.game.entity.Winner;
import com.WhoisntCitizen_server.game.event.GameEndedEvent;
import com.WhoisntCitizen_server.game.result.GameResultRecorder;
import com.WhoisntCitizen_server.jobs.domain.Faction;
import org.junit.jupiter.api.Test;
import org.mockito.InOrder;

import java.util.ArrayList;
import java.util.List;
import java.util.OptionalInt;

import static org.assertj.core.api.Assertions.assertThat;
import static org.mockito.ArgumentMatchers.any;
import static org.mockito.ArgumentMatchers.anyString;
import static org.mockito.Mockito.inOrder;
import static org.mockito.Mockito.mock;
import static org.mockito.Mockito.never;
import static org.mockito.Mockito.verify;
import static org.mockito.Mockito.when;

/** 9. 전적 저장: 취소된 게임은 반영하지 않고, 반영할 게임은 결과를 남긴 뒤 반영한다. */
class UserStatsListenerTest {

    private static final List<GameEndedEvent.PlayerOutcome> OUTCOMES = List.of(
            new GameEndedEvent.PlayerOutcome(1L, "PIRATE_RAIDER", Faction.PIRATE, true, false),
            new GameEndedEvent.PlayerOutcome(2L, "CREW_SAILOR", Faction.CREW, true, false));

    private final UserStatsService stats = mock(UserStatsService.class);
    private final GameResultRecorder recorder = mock(GameResultRecorder.class);
    private final List<Object> notices = new ArrayList<>();
    private final UserStatsListener listener = new UserStatsListener(stats, notices::add, recorder);

    @Test
    void 취소된_게임은_전적에_반영하지_않는다() {
        listener.onGameEnded(new GameEndedEvent("g-1", "1", null, GameEndReason.CANCELLED_NO_DEATHS, 10, true, OUTCOMES));

        verify(recorder, never()).record(any());
        verify(stats, never()).applyGameResult(anyString());
    }

    @Test
    void 승리_팀이_정해진_게임은_결과를_남긴_뒤_전적에_반영한다() {
        GameEndedEvent event = new GameEndedEvent("g-1", "1", Winner.CREW, GameEndReason.WIN, 3, true, OUTCOMES);
        when(stats.applyGameResult("g-1")).thenReturn(OptionalInt.of(2));

        listener.onGameEnded(event);

        InOrder order = inOrder(recorder, stats);
        order.verify(recorder).record(event);
        order.verify(stats).applyGameResult("g-1");
        assertThat(notices).singleElement().isInstanceOf(RoomNoticeEvent.class);
    }

    @Test
    void 이미_반영된_게임이면_전적_저장_안내를_다시_보내지_않는다() {
        when(stats.applyGameResult("g-1")).thenReturn(OptionalInt.empty());

        listener.onGameEnded(new GameEndedEvent("g-1", "1", Winner.CREW, GameEndReason.WIN, 3, true, OUTCOMES));

        assertThat(notices).isEmpty();
    }
}
