package com.WhoisntCitizen_server.vote.service;

import com.WhoisntCitizen_server.common.exception.GameNotFoundException;
import com.WhoisntCitizen_server.common.exception.GameRuleException;
import com.WhoisntCitizen_server.game.entity.Game;
import com.WhoisntCitizen_server.game.lock.GameLock;
import com.WhoisntCitizen_server.game.repository.GameRepository;
import com.WhoisntCitizen_server.game.service.GameFlowService;
import com.WhoisntCitizen_server.vote.dto.ExecutionResultResponse;
import com.WhoisntCitizen_server.vote.dto.VoteResponse;
import com.WhoisntCitizen_server.vote.entity.ExecutionResult;
import org.springframework.stereotype.Service;

import java.util.List;
import java.util.Map;

/** 6. 투표 / 7. 처형 결과 */
@Service
public class VoteService {

    private final GameRepository gameRepository;
    private final GameFlowService gameFlowService;
    private final GameLock gameLock;

    public VoteService(GameRepository gameRepository, GameFlowService gameFlowService, GameLock gameLock) {
        this.gameRepository = gameRepository;
        this.gameFlowService = gameFlowService;
        this.gameLock = gameLock;
    }

    /** 6. 투표 (재투표 시 덮어쓰기). 전원 투표하면 바로 7. 처형 → 8. 승리 검사 → 9. 반복/종료 */
    public VoteResponse vote(String gameId, Long voterId, Long targetId) {
        return gameLock.withLock(gameId, () -> {
            Game game = findGame(gameId);
            game.recordVote(voterId, targetId);
            gameRepository.save(game);
            if (game.allVotesSubmitted()) {
                gameFlowService.resolveVote(game);
            }
            return new VoteResponse(true, game.getPhase(), game.getPhaseVersion());
        });
    }

    /** 7. 가장 최근 처형 결과 */
    public ExecutionResultResponse getExecutionResult(String gameId) {
        return gameLock.withLock(gameId, () -> {
            Game game = findGame(gameId);
            ExecutionResult executionResult = game.getLastExecutionResult();
            if (executionResult == null) {
                throw new GameRuleException("아직 처형 결과가 없습니다.");
            }
            String name = executionResult.executedPlayerId() == null ? null : game.getPlayer(executionResult.executedPlayerId()).getNickname();
            return new ExecutionResultResponse(executionResult.day(), executionResult.executedPlayerId(), name, executionResult.tie(),
                    executionResult.voteCounts(), voteList(game, executionResult.voteCounts()));
        });
    }

    /** 득표 많은 순, 같으면 playerId 순. JsonUtility가 Map을 못 읽어서 클라이언트에는 이 목록을 쓴다. */
    private List<ExecutionResultResponse.VoteCount> voteList(Game game, Map<Long, Integer> voteCounts) {
        return voteCounts.entrySet().stream()
                .sorted(Map.Entry.<Long, Integer>comparingByValue().reversed()
                        .thenComparing(Map.Entry.comparingByKey()))
                .map(e -> new ExecutionResultResponse.VoteCount(e.getKey(), game.getPlayer(e.getKey()).getNickname(), e.getValue()))
                .toList();
    }

    private Game findGame(String gameId) {
        return gameRepository.findById(gameId).orElseThrow(() -> new GameNotFoundException(gameId));
    }
}
