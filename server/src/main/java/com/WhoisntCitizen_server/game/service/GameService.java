package com.WhoisntCitizen_server.game.service;

import com.WhoisntCitizen_server.common.exception.GameNotFoundException;
import com.WhoisntCitizen_server.common.exception.GameRuleException;
import com.WhoisntCitizen_server.game.dto.GameResultResponse;
import com.WhoisntCitizen_server.game.dto.GameStateResponse;
import com.WhoisntCitizen_server.game.dto.MyRoleResponse;
import com.WhoisntCitizen_server.game.dto.StartGameRequest;
import com.WhoisntCitizen_server.game.dto.StartGameResponse;
import com.WhoisntCitizen_server.game.entity.Game;
import com.WhoisntCitizen_server.game.entity.GamePlayer;
import com.WhoisntCitizen_server.game.repository.GameRepository;
import com.WhoisntCitizen_server.jobs.domain.RoleDefinition;
import org.springframework.stereotype.Service;

import java.util.ArrayList;
import java.util.HashSet;
import java.util.List;
import java.util.Set;

/** 1. 게임 시작, 2. 역할 배정/조회, 3·5·6·9 상태 조회, 8·9 게임 결과 */
@Service
public class GameService {

    private final GameRepository gameRepository;
    private final RoleAssigner roleAssigner;
    private final GameFlowService gameFlowService;

    public GameService(GameRepository gameRepository, RoleAssigner roleAssigner, GameFlowService gameFlowService) {
        this.gameRepository = gameRepository;
        this.roleAssigner = roleAssigner;
        this.gameFlowService = gameFlowService;
    }

    /** 1. 게임 시작 + 2. 역할 배정 → 첫 밤 진입 */
    public StartGameResponse startGame(StartGameRequest request) {
        List<StartGameRequest.PlayerEntry> entries = request.players();

        //수정될 부분 - id 중복처리
        Set<Long> ids = new HashSet<>();
        for (StartGameRequest.PlayerEntry e : entries) {
            if (!ids.add(e.playerId())) {
                throw new GameRuleException("중복된 playerId가 있습니다: " + e.playerId());
            }
        }

        List<RoleDefinition> roles = roleAssigner.assign(entries.size());
        List<GamePlayer> players = new ArrayList<>();
        for (int i = 0; i < entries.size(); i++) {
            players.add(new GamePlayer(entries.get(i).playerId(), entries.get(i).nickname(), roles.get(i)));
        }

        //수정될 부분 - Repository DB 연동
        Game game = gameRepository.save(new Game(request.roomId(), players));
        gameFlowService.begin(game);

        synchronized (game) {
            return new StartGameResponse(game.getGameId(), game.getPhase(), game.getDay(), game.getPhaseEndsAt());
        }
    }

    /** 3/5/6/9. 현재 페이즈와 공개 정보 */
    public GameStateResponse getState(String gameId) {
        Game game = findGame(gameId);
        synchronized (game) {
            return GameStateResponse.from(game);
        }
    }

    /** 2. 내 역할 조회 (본인만) */
    public MyRoleResponse getMyRole(String gameId, Long playerId) {
        Game game = findGame(gameId);
        synchronized (game) {
            GamePlayer me = game.getPlayer(playerId);
            List<Long> teammates = me.isPirate()
                    ? game.getPlayers().stream()
                        .filter(p -> p.isPirate() && !p.getPlayerId().equals(playerId))
                        .map(GamePlayer::getPlayerId).toList()
                    : List.of();
            return MyRoleResponse.of(me, teammates);
        }
    }



    /** 8~9. 게임 결과. 종료 전에는 ended=false, 역할 비공개. */
    public GameResultResponse getResult(String gameId) {
        Game game = findGame(gameId);
        synchronized (game) {
            if (!game.isEnded()) {
                return new GameResultResponse(false, null, game.getDay(), List.of());
            }
            List<GameResultResponse.PlayerResult> results = game.getPlayers().stream()
                    .map(GameResultResponse.PlayerResult::from)
                    .toList();
            return new GameResultResponse(true, game.getWinner(), game.getDay(), results);
        }
    }

    private Game findGame(String gameId) {
        return gameRepository.findById(gameId).orElseThrow(() -> new GameNotFoundException(gameId));
    }
}
