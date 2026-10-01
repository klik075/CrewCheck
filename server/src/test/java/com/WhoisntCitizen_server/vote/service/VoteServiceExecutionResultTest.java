package com.WhoisntCitizen_server.vote.service;

import com.WhoisntCitizen_server.game.entity.Game;
import com.WhoisntCitizen_server.game.entity.GamePlayer;
import com.WhoisntCitizen_server.game.repository.InMemoryGameRepository;
import com.WhoisntCitizen_server.game.service.GameFlowService;
import com.WhoisntCitizen_server.jobs.domain.ActionCode;
import com.WhoisntCitizen_server.jobs.domain.Faction;
import com.WhoisntCitizen_server.jobs.domain.RoleDefinition;
import com.WhoisntCitizen_server.vote.dto.ExecutionResultResponse;
import com.WhoisntCitizen_server.vote.dto.ExecutionResultResponse.VoteCount;
import com.WhoisntCitizen_server.vote.entity.ExecutionResult;
import org.junit.jupiter.api.BeforeEach;
import org.junit.jupiter.api.Test;

import java.util.List;
import java.util.Map;

import static org.assertj.core.api.Assertions.assertThat;
import static org.mockito.Mockito.mock;

/** S-A: 처형 결과의 득표 목록(votes). JsonUtility가 Map(voteCounts)을 읽지 못해서 추가했다. */
class VoteServiceExecutionResultTest {

    private static final RoleDefinition RAIDER =
            new RoleDefinition("PIRATE_RAIDER", "해적", Faction.PIRATE, ActionCode.SELECT_ATTACK_TARGET);
    private static final RoleDefinition SAILOR =
            new RoleDefinition("CREW_SAILOR", "선원", Faction.CREW, null);

    private InMemoryGameRepository repository;
    private VoteService voteService;
    private Game game;

    @BeforeEach
    void setUp() {
        repository = new InMemoryGameRepository();
        voteService = new VoteService(repository, mock(GameFlowService.class));
        game = new Game("room-1", List.of(
                new GamePlayer(1L, "p1", RAIDER),
                new GamePlayer(2L, "p2", SAILOR),
                new GamePlayer(3L, "p3", SAILOR),
                new GamePlayer(4L, "p4", SAILOR)));
        repository.save(game);
    }

    @Test
    void 득표_목록은_많은_순이고_같으면_playerId_순이며_닉네임이_들어간다() {
        game.setLastExecutionResult(new ExecutionResult(1, 3L, false, Map.of(4L, 1, 3L, 2, 2L, 1)));

        ExecutionResultResponse r = voteService.getExecutionResult(game.getGameId());

        assertThat(r.votes()).containsExactly(
                new VoteCount(3L, "p3", 2),
                new VoteCount(2L, "p2", 1),
                new VoteCount(4L, "p4", 1));
        assertThat(r.executedNickname()).isEqualTo("p3");
    }

    @Test
    void 기존_voteCounts도_그대로_내려간다() {
        Map<Long, Integer> counts = Map.of(2L, 2, 3L, 2);
        game.setLastExecutionResult(new ExecutionResult(1, null, true, counts));

        ExecutionResultResponse r = voteService.getExecutionResult(game.getGameId());

        assertThat(r.voteCounts()).isEqualTo(counts);
        assertThat(r.tie()).isTrue();
        assertThat(r.executedPlayerId()).isNull();
    }

    @Test
    void 아무도_투표하지_않았으면_빈_목록이다() {
        game.setLastExecutionResult(new ExecutionResult(1, null, false, Map.of()));

        ExecutionResultResponse r = voteService.getExecutionResult(game.getGameId());

        assertThat(r.votes()).isEmpty();
    }
}