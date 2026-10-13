package com.WhoisntCitizen_server.game.service;

import com.WhoisntCitizen_server.game.dto.GameStateResponse;
import com.WhoisntCitizen_server.game.activity.LocalPlayerActivityTracker;
import com.WhoisntCitizen_server.game.entity.Game;
import com.WhoisntCitizen_server.game.entity.GamePhase;
import com.WhoisntCitizen_server.game.entity.GamePlayer;
import com.WhoisntCitizen_server.game.lock.LocalGameLock;
import com.WhoisntCitizen_server.game.repository.InMemoryGameRepository;
import com.WhoisntCitizen_server.jobs.domain.ActionCode;
import com.WhoisntCitizen_server.jobs.domain.Faction;
import com.WhoisntCitizen_server.jobs.domain.RoleDefinition;
import org.junit.jupiter.api.Test;

import java.time.Clock;
import java.time.Duration;
import java.time.Instant;
import java.time.ZoneOffset;
import java.util.List;

import static org.assertj.core.api.Assertions.assertThat;
import static org.mockito.Mockito.mock;

/** S1: 게임 상태의 serverTime. 클라이언트는 phaseEndsAt - serverTime으로 남은 시간을 계산한다. */
class GameServiceStateTest {

    private static final RoleDefinition RAIDER =
            new RoleDefinition("PIRATE_RAIDER", "해적", Faction.PIRATE, ActionCode.SELECT_ATTACK_TARGET);
    private static final RoleDefinition SAILOR =
            new RoleDefinition("CREW_SAILOR", "선원", Faction.CREW, null);

    private static final Instant NOW = Instant.parse("2026-10-01T12:00:00Z");

    @Test
    void 상태_응답에_서버_시각이_들어가고_남은_시간을_계산할_수_있다() {
        InMemoryGameRepository repository = new InMemoryGameRepository();
        GameService gameService = new GameService(repository, mock(RoleAssigner.class), mock(GameFlowService.class),
                new LocalGameLock(), new LocalPlayerActivityTracker(), Clock.fixed(NOW, ZoneOffset.UTC));
        Game game = new Game("room-1", List.of(
                new GamePlayer(1L, "p1", RAIDER),
                new GamePlayer(2L, "p2", SAILOR),
                new GamePlayer(3L, "p3", SAILOR),
                new GamePlayer(4L, "p4", SAILOR)));
        game.changePhase(GamePhase.NIGHT, NOW.plusSeconds(30));
        repository.save(game);

        GameStateResponse state = gameService.getState(game.getGameId());

        assertThat(state.serverTime()).isEqualTo(NOW);
        assertThat(Duration.between(state.serverTime(), state.phaseEndsAt())).isEqualTo(Duration.ofSeconds(30));
    }

    @Test
    void 상태를_조회한_플레이어는_접속한_것으로_기록된다() {
        InMemoryGameRepository repository = new InMemoryGameRepository();
        LocalPlayerActivityTracker tracker = new LocalPlayerActivityTracker();
        GameService gameService = new GameService(repository, mock(RoleAssigner.class), mock(GameFlowService.class),
                new LocalGameLock(), tracker, Clock.fixed(NOW, ZoneOffset.UTC));
        Game game = new Game("room-1", List.of(
                new GamePlayer(1L, "p1", RAIDER),
                new GamePlayer(2L, "p2", SAILOR)));
        tracker.markAllSeen(game.getGameId(), List.of(1L, 2L), NOW.minusSeconds(120));
        repository.save(game);

        gameService.getState(game.getGameId(), 2L);
        gameService.getState(game.getGameId(), 99L); // 참가자가 아니면 기록하지 않는다

        assertThat(tracker.lastSeen(game.getGameId())).doesNotContainKey(99L);
        assertThat(game.inactivePlayers(tracker.lastSeen(game.getGameId()), NOW.minusSeconds(60)))
                .extracting(GamePlayer::getPlayerId).containsExactly(1L);
    }
}