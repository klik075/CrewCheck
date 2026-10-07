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

import java.util.List;
import java.util.Map;

/** 6. 투표 / 7. 처형 결과 */
@Service
public class VoteService {

    private final GameRepository gameRepository;
    private final GameFlowService gameFlowService;

    public VoteService(GameRepository gameRepository, GameFlowService gameFlowService) {
        this.gameRepository = gameRepository;
        this.gameFlowService = gameFlowService;
    }

    /** 6. 투표 + 투표 완료 (기존 방식) */
    public VoteResponse vote(String gameId, Long voterId, Long targetId) {
        return vote(gameId, voterId, targetId, true);
    }

    /**
     * 6. 투표 (재투표 시 덮어쓰기). targetId=null이면 표를 거둔다(기권).
     * confirm=true("투표 완료")면 지금 상태로 고정한다. 투표할 수 있는 생존자가 모두 완료하면 바로 7. 처형 → 8. 승리 검사 → 9. 반복/종료
     */
    public VoteResponse vote(String gameId, Long voterId, Long targetId, boolean confirm) {
        Game game = findGame(gameId);
        synchronized (game) {
            game.recordVote(voterId, targetId, confirm);
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
            return new ExecutionResultResponse(executionResult.day(), executionResult.executedPlayerId(), name, executionResult.tie(),
                    executionResult.voteCounts(), voteList(game, executionResult.voteCounts()));
        }
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
