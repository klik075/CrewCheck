package com.WhoisntCitizen_server.vote.service;

import com.WhoisntCitizen_server.common.exception.GameNotFoundException;
import com.WhoisntCitizen_server.common.exception.GameRuleException;
import com.WhoisntCitizen_server.game.entity.Game;
import com.WhoisntCitizen_server.game.repository.GameRepository;
import com.WhoisntCitizen_server.game.service.GameFlowService;
import com.WhoisntCitizen_server.vote.dto.ExecutionResultResponse;
import com.WhoisntCitizen_server.vote.dto.VoteResponse;
import com.WhoisntCitizen_server.vote.entity.ExecutionResult;
import org.springframework.stereotype.Service;

/** 6. 투표 / 7. 처형 결과 */
@Service
public class VoteService {

    private final GameRepository gameRepository;
    private final GameFlowService gameFlowService;

    public VoteService(GameRepository gameRepository, GameFlowService gameFlowService) {
        this.gameRepository = gameRepository;
        this.gameFlowService = gameFlowService;
    }

    /** 6. 투표 (재투표 시 덮어쓰기). 전원 투표하면 바로 7. 처형 → 8. 승리 검사 → 9. 반복/종료 */
    public VoteResponse vote(String gameId, Long voterId, Long targetId) {
        Game game = findGame(gameId);
        synchronized (game) {
            game.recordVote(voterId, targetId);
            gameRepository.save(game);
            if (game.allVotesSubmitted()) {
                gameFlowService.resolveVote(game);
            }
            return new VoteResponse(true, game.getPhase(), game.getPhaseVersion());
        }
    }

    /** 7. 가장 최근 처형 결과 */
    public ExecutionResultResponse getExecutionResult(String gameId) {
        Game game = findGame(gameId);
        synchronized (game) {
            ExecutionResult executionResult = game.getLastExecutionResult();
            if (executionResult == null) {
                throw new GameRuleException("아직 처형 결과가 없습니다.");
            }
            String name = executionResult.executedPlayerId() == null ? null : game.getPlayer(executionResult.executedPlayerId()).getNickname();
            return new ExecutionResultResponse(executionResult.day(), executionResult.executedPlayerId(), name, executionResult.tie(), executionResult.voteCounts());
        }
    }

    private Game findGame(String gameId) {
        return gameRepository.findById(gameId).orElseThrow(() -> new GameNotFoundException(gameId));
    }
}
