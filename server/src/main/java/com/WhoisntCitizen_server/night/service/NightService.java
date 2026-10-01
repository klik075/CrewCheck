package com.WhoisntCitizen_server.night.service;

import com.WhoisntCitizen_server.common.exception.GameNotFoundException;
import com.WhoisntCitizen_server.common.exception.GameRuleException;
import com.WhoisntCitizen_server.game.entity.Game;
import com.WhoisntCitizen_server.game.entity.GamePlayer;
import com.WhoisntCitizen_server.game.repository.GameRepository;
import com.WhoisntCitizen_server.game.service.GameFlowService;
import com.WhoisntCitizen_server.night.dto.NightActionResponse;
import com.WhoisntCitizen_server.night.dto.NightResultResponse;
import com.WhoisntCitizen_server.night.entity.NightResult;
import org.springframework.stereotype.Service;
import com.WhoisntCitizen_server.night.entity.PrivateReport;
import java.time.Clock;
import java.util.List;

/** 3. 밤 능력 사용 / 4. 밤 결과 공개 */
@Service
public class NightService {

    private final GameRepository gameRepository;
    private final GameFlowService gameFlowService;
    private final Clock clock;

    public NightService(GameRepository gameRepository, GameFlowService gameFlowService, Clock clock) {
        this.gameRepository = gameRepository;
        this.gameFlowService = gameFlowService;
        this.clock = clock;
    }

    /** 3. 밤 능력 사용. 앵무새가 해적을 지목하면 즉시 접선하고, 알게 된 해적 id를 응답으로 알려 준다. */
    public NightActionResponse submitAction(String gameId, Long actorId, Long targetId) {
        Game game = findGame(gameId);
        synchronized (game) {
            boolean contacted = game.recordNightAction(actorId, targetId, clock.instant());
            gameRepository.save(game);
            // 전원 제출 시 타이머를 기다리지 않고 바로 결과 공개.
            // 단, 접선 직후에는 해적이 표를 바꿀 수 있도록 조기 판정하지 않는다(다음 제출이나 타이머가 판정).
            if (!contacted && game.allNightActionsSubmitted()) {
                gameFlowService.resolveNight(game);
            }
            List<Long> contactedPirateIds = contacted
                    ? game.knownPirateAllies(game.getPlayer(actorId)).stream().map(GamePlayer::getPlayerId).toList()
                    : List.of();
            return new NightActionResponse(true, game.getPhase(), game.getPhaseVersion(), contactedPirateIds);
        }
    }

    /** 3. 이번 밤 능력을 쓰지 않고 넘기기. 마지막으로 남은 사람이 넘기면 바로 결과 공개. */
    public NightActionResponse skipAction(String gameId, Long actorId) {
        Game game = findGame(gameId);
        synchronized (game) {
            game.skipNightAction(actorId);
            gameRepository.save(game);
            if (game.allNightActionsSubmitted()) {
                gameFlowService.resolveNight(game);
            }
            return new NightActionResponse(true, game.getPhase(), game.getPhaseVersion(), List.of());
        }
    }

    /** 4. 가장 최근 밤 결과. 개인 결과(reports)는 요청자 본인 것만 포함한다. */
    public NightResultResponse getNightResult(String gameId, Long requesterId) {
        Game game = findGame(gameId);
        synchronized (game) {
            GamePlayer requester = game.getPlayer(requesterId);
            NightResult nightResult = game.getLastNightResult();
            if (nightResult == null) {
                throw new GameRuleException("아직 공개된 밤 결과가 없습니다.");
            }
            List<NightResultResponse.ReportView> myReports = nightResult.reportsFor(requester.getPlayerId()).stream()
                    .map(r -> toView(game, r))
                    .toList();
            return new NightResultResponse(nightResult.day(), nightResult.killedPlayerId(),
                    nicknameOf(game, nightResult.killedPlayerId()), nightResult.protectedByDoctor(), myReports);
        }
    }

    private NightResultResponse.ReportView toView(Game game, PrivateReport r) {
        List<NightResultResponse.PlayerRef> players = r.playerIds().stream()
                .map(id -> new NightResultResponse.PlayerRef(id, nicknameOf(game, id)))
                .toList();
        List<NightResultResponse.ActionView> actions = r.actions().stream()
                .map(a -> new NightResultResponse.ActionView(a.code(), a.targetId(), nicknameOf(game, a.targetId())))
                .toList();
        return new NightResultResponse.ReportView(r.type(), r.targetId(), nicknameOf(game, r.targetId()),
                r.faction(), r.roleCode(), r.roleName(), players, actions);
    }

    private String nicknameOf(Game game, Long playerId) {
        return playerId == null ? null : game.getPlayer(playerId).getNickname();
    }

    private Game findGame(String gameId) {
        return gameRepository.findById(gameId).orElseThrow(() -> new GameNotFoundException(gameId));
    }
}