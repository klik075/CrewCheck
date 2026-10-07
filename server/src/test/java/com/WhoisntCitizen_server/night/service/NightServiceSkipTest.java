package com.WhoisntCitizen_server.night.service;

import com.WhoisntCitizen_server.game.entity.Game;
import com.WhoisntCitizen_server.game.entity.GamePhase;
import com.WhoisntCitizen_server.game.entity.GamePlayer;
import com.WhoisntCitizen_server.game.lock.LocalGameLock;
import com.WhoisntCitizen_server.game.repository.InMemoryGameRepository;
import com.WhoisntCitizen_server.game.service.GameFlowService;
import com.WhoisntCitizen_server.jobs.domain.ActionCode;
import com.WhoisntCitizen_server.jobs.domain.Faction;
import com.WhoisntCitizen_server.jobs.domain.RoleDefinition;
import org.junit.jupiter.api.BeforeEach;
import org.junit.jupiter.api.Test;

import java.time.Clock;
import java.time.Instant;
import java.time.ZoneOffset;
import java.util.List;

import static org.mockito.ArgumentMatchers.any;
import static org.mockito.Mockito.mock;
import static org.mockito.Mockito.never;
import static org.mockito.Mockito.verify;

/** 6단계: 넘기기로 마지막 사람이 끝나면 밤을 바로 끝낸다. */
class NightServiceSkipTest {

    private static final RoleDefinition RAIDER =
            new RoleDefinition("PIRATE_RAIDER", "해적", Faction.PIRATE, ActionCode.SELECT_ATTACK_TARGET);
    private static final RoleDefinition CAPTAIN =
            new RoleDefinition("CREW_CAPTAIN", "선장", Faction.CREW, ActionCode.INVESTIGATE_FACTION);
    private static final RoleDefinition SAILOR =
            new RoleDefinition("CREW_SAILOR", "선원", Faction.CREW, null);

    private static final Instant NOW = Instant.parse("2026-10-01T12:00:00Z");

    // 1: 해적, 2: 선장, 3~4: 선원
    private Game game;
    private GameFlowService gameFlowService;
    private NightService nightService;

    @BeforeEach
    void setUp() {
        InMemoryGameRepository repository = new InMemoryGameRepository();
        gameFlowService = mock(GameFlowService.class);
        nightService = new NightService(repository, gameFlowService, new LocalGameLock(), Clock.fixed(NOW, ZoneOffset.UTC));

        game = new Game("room-1", List.of(
                new GamePlayer(1L, "해적", RAIDER),
                new GamePlayer(2L, "선장", CAPTAIN),
                new GamePlayer(3L, "선원1", SAILOR),
                new GamePlayer(4L, "선원2", SAILOR)));
        game.changePhase(GamePhase.NIGHT, NOW.plusSeconds(30));
        repository.save(game);
    }

    @Test
    void 아직_남은_사람이_있으면_넘겨도_밤이_계속된다() {
        nightService.skipAction(game.getGameId(), 2L);

        verify(gameFlowService, never()).resolveNight(any());
    }

    @Test
    void 마지막_사람이_넘기면_바로_밤을_끝낸다() {
        nightService.submitAction(game.getGameId(), 1L, 3L);
        nightService.skipAction(game.getGameId(), 2L);

        verify(gameFlowService).resolveNight(game);
    }
}