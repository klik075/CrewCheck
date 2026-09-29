package com.sparta.unityaitestproject_server.night.service;

import com.sparta.unityaitestproject_server.common.exception.GameNotFoundException;
import com.sparta.unityaitestproject_server.common.exception.GameRuleException;
import com.sparta.unityaitestproject_server.game.entity.Game;
import com.sparta.unityaitestproject_server.game.entity.GamePlayer;
import com.sparta.unityaitestproject_server.game.repository.GameRepository;
import com.sparta.unityaitestproject_server.game.service.GameFlowService;
import com.sparta.unityaitestproject_server.night.dto.NightActionResponse;
import com.sparta.unityaitestproject_server.night.dto.NightResultResponse;
import com.sparta.unityaitestproject_server.night.entity.NightResult;
import org.springframework.stereotype.Service;

/** 3. 밤 능력 사용 / 4. 밤 결과 공개 */
@Service
public class NightService {

    private final GameRepository gameRepository;
    private final GameFlowService gameFlowService;

    public NightService(GameRepository gameRepository, GameFlowService gameFlowService) {
        this.gameRepository = gameRepository;
        this.gameFlowService = gameFlowService;
    }

    /** 3. 밤 능력 사용 (마피아 처치 / 경찰 조사 / 의사 보호) */
    public NightActionResponse submitAction(String gameId, Long actorId, Long targetId) {
        Game game = findGame(gameId);
        synchronized (game) {
            game.recordNightAction(actorId, targetId);
            gameRepository.save(game);
            if (game.allNightActionsSubmitted()) {
                gameFlowService.resolveNight(game); // 전원 제출 시 타이머를 기다리지 않고 바로 결과 공개
            }
            return new NightActionResponse(true, game.getPhase(), game.getPhaseVersion());
        }
    }

    /** 4. 가장 최근 밤 결과. 조사 결과는 요청자가 그 밤에 조사한 경찰일 때만 포함한다. */
    public NightResultResponse getNightResult(String gameId, Long requesterId) {
        Game game = findGame(gameId);
        synchronized (game) {
            GamePlayer requester = game.getPlayer(requesterId);
            NightResult nightResult = game.getLastNightResult();
            if (nightResult == null) {
                throw new GameRuleException("아직 공개된 밤 결과가 없습니다.");
            }
            NightResultResponse.Investigation inv = null;
            NightResult.Investigation mine = nightResult.investigations().get(requester.getPlayerId());
            if (mine != null) {
                inv = new NightResultResponse.Investigation(mine.targetId(),
                        game.getPlayer(mine.targetId()).getNickname(), mine.mafia());
            }
            String killedName = nightResult.killedPlayerId() == null ? null : game.getPlayer(nightResult.killedPlayerId()).getNickname();
            return new NightResultResponse(nightResult.day(), nightResult.killedPlayerId(), killedName, nightResult.protectedByDoctor(), inv);
        }
    }

    private Game findGame(String gameId) {
        return gameRepository.findById(gameId).orElseThrow(() -> new GameNotFoundException(gameId));
    }
}
