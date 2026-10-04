package com.WhoisntCitizen_server.game.service;

import com.WhoisntCitizen_server.game.dto.DevRoleView;
import com.WhoisntCitizen_server.game.dto.GameParticipant;
import com.WhoisntCitizen_server.game.dto.RoleComposition;
import com.WhoisntCitizen_server.game.dto.RoleSetup;
import com.WhoisntCitizen_server.game.dto.RoleSetupMode;
import com.WhoisntCitizen_server.game.dto.StartGameRequest;
import com.WhoisntCitizen_server.game.repository.InMemoryGameRepository;
import com.WhoisntCitizen_server.support.TestRoles;
import org.junit.jupiter.api.BeforeEach;
import org.junit.jupiter.api.Test;

import java.time.Clock;
import java.util.List;
import java.util.stream.LongStream;

import static org.assertj.core.api.Assertions.assertThat;
import static org.assertj.core.api.Assertions.assertThatThrownBy;
import static org.mockito.Mockito.mock;

/** 게임 시작 시 직업 배정 설정 전달: 로비용 startGame(…, roleSetup)과 개발용 POST /api/v1/games의 roleSetup */
class GameServiceRoleSetupTest {

    private static final List<String> FOUR_DOCTORS = List.of("PIRATE_RAIDER", "CREW_DOCTOR", "CREW_DOCTOR", "CREW_CAPTAIN");

    private InMemoryGameRepository repository;
    private GameService gameService;

    @BeforeEach
    void setUp() {
        repository = new InMemoryGameRepository();
        gameService = new GameService(repository, TestRoles.assigner(7), mock(GameFlowService.class), Clock.systemUTC());
    }

    private static List<GameParticipant> participants(int count) {
        return LongStream.rangeClosed(1, count).mapToObj(id -> new GameParticipant(id, "p" + id)).toList();
    }

    private static List<StartGameRequest.PlayerEntry> entries(int count) {
        return LongStream.rangeClosed(1, count).mapToObj(id -> new StartGameRequest.PlayerEntry(id, "p" + id)).toList();
    }

    private List<String> sortedRoles(String gameId) {
        return gameService.getDevRoles(gameId).stream().map(DevRoleView::role).sorted().toList();
    }

    @Test
    void 설정_없이_시작하면_추천_구성이다() {
        String gameId = gameService.startGame("1", participants(4)).gameId();

        assertThat(sortedRoles(gameId)).containsExactly("CREW_CAPTAIN", "CREW_DOCTOR", "CREW_SAILOR", "PIRATE_RAIDER");
    }

    @Test
    void 방의_설정을_넘기면_그_설정대로_배정한다() {
        RoleSetup setup = new RoleSetup(RoleSetupMode.CUSTOM, List.of(new RoleComposition(4, FOUR_DOCTORS)), null);

        String gameId = gameService.startGame("1", participants(4), setup).gameId();

        assertThat(sortedRoles(gameId)).containsExactly("CREW_CAPTAIN", "CREW_DOCTOR", "CREW_DOCTOR", "PIRATE_RAIDER");
    }

    @Test
    void 개발용_시작_요청의_랜덤_설정도_검증_후_그대로_쓴다() {
        RoleSetup randomWithoutSpecials = new RoleSetup(RoleSetupMode.RANDOM, null, List.of());

        String gameId = gameService.startGame(new StartGameRequest("dev", entries(6), randomWithoutSpecials)).gameId();

        // 6인 추천 구성의 해적 진영은 2명. 특수 직업 후보가 없으면 해적 2 + 선원 4
        assertThat(sortedRoles(gameId)).containsExactly(
                "CREW_SAILOR", "CREW_SAILOR", "CREW_SAILOR", "CREW_SAILOR", "PIRATE_RAIDER", "PIRATE_RAIDER");
    }

    @Test
    void 개발용_시작_요청의_설정이_규칙에_맞지_않으면_게임을_만들지_않는다() {
        RoleSetup invalid = new RoleSetup(RoleSetupMode.CUSTOM,
                List.of(new RoleComposition(4, List.of("PIRATE_RAIDER", "PIRATE_PARROT", "CREW_CAPTAIN", "CREW_SAILOR"))), null);

        assertThatThrownBy(() -> gameService.startGame(new StartGameRequest("dev", entries(4), invalid)))
                .isInstanceOf(IllegalArgumentException.class)
                .hasMessageContaining("해적 진영");
        assertThat(repository.findAll()).isEmpty();
    }
}
