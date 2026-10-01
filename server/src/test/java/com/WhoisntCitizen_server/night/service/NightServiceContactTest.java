package com.WhoisntCitizen_server.night.service;

import com.WhoisntCitizen_server.game.entity.Game;
import com.WhoisntCitizen_server.game.entity.GamePhase;
import com.WhoisntCitizen_server.game.entity.GamePlayer;
import com.WhoisntCitizen_server.game.repository.InMemoryGameRepository;
import com.WhoisntCitizen_server.game.service.GameFlowService;
import com.WhoisntCitizen_server.jobs.domain.ActionCode;
import com.WhoisntCitizen_server.jobs.domain.Faction;
import com.WhoisntCitizen_server.jobs.domain.RoleDefinition;
import com.WhoisntCitizen_server.night.dto.NightActionResponse;
import org.junit.jupiter.api.BeforeEach;
import org.junit.jupiter.api.Test;

import java.time.Clock;
import java.time.Instant;
import java.time.ZoneOffset;
import java.util.List;

import static org.assertj.core.api.Assertions.assertThat;
import static org.mockito.ArgumentMatchers.any;
import static org.mockito.Mockito.mock;
import static org.mockito.Mockito.never;
import static org.mockito.Mockito.verify;

/** 4단계: 접선이 일어난 제출은 전원 제출이어도 밤을 바로 끝내지 않는다. */
class NightServiceContactTest {

    private static final RoleDefinition RAIDER =
            new RoleDefinition("PIRATE_RAIDER", "해적", Faction.PIRATE, ActionCode.SELECT_ATTACK_TARGET);
    private static final RoleDefinition PARROT =
            new RoleDefinition("PIRATE_PARROT", "앵무새", Faction.PIRATE, ActionCode.WATCH_ACTION);
    private static final RoleDefinition SAILOR =
            new RoleDefinition("CREW_SAILOR", "선원", Faction.CREW, null);

    private static final Instant NOW = Instant.parse("2026-10-01T12:00:00Z");

    // 1: 해적, 2: 앵무새, 3~4: 선원
    private Game game;
    private GameFlowService gameFlowService;
    private NightService nightService;

    @BeforeEach
    void setUp() {
        InMemoryGameRepository repository = new InMemoryGameRepository();
        gameFlowService = mock(GameFlowService.class);
        nightService = new NightService(repository, gameFlowService, Clock.fixed(NOW, ZoneOffset.UTC));

        game = new Game("room-1", List.of(
                new GamePlayer(1L, "해적", RAIDER),
                new GamePlayer(2L, "앵무새", PARROT),
                new GamePlayer(3L, "선원1", SAILOR),
                new GamePlayer(4L, "선원2", SAILOR)));
        game.changePhase(GamePhase.NIGHT, NOW.plusSeconds(30));
        repository.save(game);
    }

    @Test
    void 접선이_없으면_전원_제출_시_바로_밤을_끝낸다() {
        nightService.submitAction(game.getGameId(), 1L, 3L);
        NightActionResponse response = nightService.submitAction(game.getGameId(), 2L, 3L);

        verify(gameFlowService).resolveNight(game);
        assertThat(response.contactedPirateIds()).isEmpty();
    }

    @Test
    void 접선한_제출은_전원_제출이어도_밤을_바로_끝내지_않는다() {
        nightService.submitAction(game.getGameId(), 1L, 3L);
        NightActionResponse response = nightService.submitAction(game.getGameId(), 2L, 1L);

        verify(gameFlowService, never()).resolveNight(any());
        assertThat(response.contactedPirateIds()).containsExactly(1L);
        assertThat(game.getPlayer(2L).getContactedAt()).isEqualTo(NOW);
    }

    @Test
    void 접선_후_해적이_다시_제출하면_그때_밤을_끝낸다() {
        nightService.submitAction(game.getGameId(), 1L, 3L);
        nightService.submitAction(game.getGameId(), 2L, 1L);

        NightActionResponse response = nightService.submitAction(game.getGameId(), 1L, 4L);

        verify(gameFlowService).resolveNight(game);
        assertThat(response.contactedPirateIds()).isEmpty();
    }

    // ---------- 능력 사용 시스템 메시지 (해적 전용) ----------

    @Test
    void 해적이_공격_대상을_고르면_해적에게_알린다() {
        nightService.submitAction(game.getGameId(), 1L, 3L);

        verify(gameFlowService).announceToPirates(game, "해적님이 선원1님을 공격 대상으로 골랐습니다.");
    }

    @Test
    void 해적이_공격_대상_선택을_넘기면_해적에게_알린다() {
        nightService.skipAction(game.getGameId(), 1L);

        verify(gameFlowService).announceToPirates(game, "해적님이 이번 밤 공격 대상 선택을 넘겼습니다.");
    }

    @Test
    void 앵무새가_접선하면_해적에게_알린다() {
        nightService.submitAction(game.getGameId(), 2L, 1L);

        verify(gameFlowService).announceToPirates(game, "앵무새 앵무새님이 해적과 접선했습니다.");
    }

    @Test
    void 접선하지_않은_앵무새의_행동은_알리지_않는다() {
        nightService.submitAction(game.getGameId(), 2L, 3L);

        verify(gameFlowService, never()).announceToPirates(any(), any());
    }
}
