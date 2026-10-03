package com.WhoisntCitizen_server.game.service;

import com.WhoisntCitizen_server.common.exception.GameNotFoundException;
import com.WhoisntCitizen_server.common.exception.GameRuleException;
import com.WhoisntCitizen_server.game.dto.DevRoleView;
import com.WhoisntCitizen_server.game.dto.GameParticipant;
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
import java.time.Clock;

/** 1. 게임 시작, 2. 역할 배정/조회, 3·5·6·9 상태 조회, 8·9 게임 결과 */
@Service
public class GameService {

    private final GameRepository gameRepository;
    private final RoleAssigner roleAssigner;
    private final GameFlowService gameFlowService;
    private final Clock clock;

    public GameService(GameRepository gameRepository, RoleAssigner roleAssigner, GameFlowService gameFlowService, Clock clock) {
        this.gameRepository = gameRepository;
        this.roleAssigner = roleAssigner;
        this.gameFlowService = gameFlowService;
        this.clock = clock;
    }

    /**
     * 1. 게임 시작 (Postman 테스트용 API: POST /api/v1/games).
     * 요청 DTO를 GameParticipant 목록으로 바꿔 서버 내부용 startGame에 위임한다.
     */
    public StartGameResponse startGame(StartGameRequest request) {
        List<GameParticipant> participants = request.players().stream()
                .map(e -> new GameParticipant(e.playerId(), e.nickname()))
                .toList();
        // 개발용 게임: 가짜 playerId가 실제 User.id와 겹칠 수 있으므로 전적에 반영하지 않는다.
        return start(request.roomId(), participants, false);
    }

    /**
     * 1. 게임 시작 + 2. 역할 배정 → 첫 밤 진입 (서버 내부용).
     * 로비의 방 시작 처리(RoomService)가 Room의 참가자 목록으로 호출한다.
     * 인원 수(4~12명)는 RoleAssigner가 검사한다.
     */
    public StartGameResponse startGame(String roomId, List<GameParticipant> participants) {
        return start(roomId, participants, true);
    }

    private StartGameResponse start(String roomId, List<GameParticipant> participants, boolean recordStats) {
        if (roomId == null || roomId.isBlank()) {
            throw new GameRuleException("roomId가 필요합니다.");
        }
        if (participants == null || participants.isEmpty()) {
            throw new GameRuleException("참가자가 없습니다.");
        }

        Set<Long> ids = new HashSet<>();
        for (GameParticipant p : participants) {
            if (p.userId() == null) {
                throw new GameRuleException("userId가 없는 참가자가 있습니다.");
            }
            if (!ids.add(p.userId())) {
                throw new GameRuleException("중복된 playerId가 있습니다: " + p.userId());
            }
        }

        List<RoleDefinition> roles = roleAssigner.assign(participants.size());
        List<GamePlayer> players = new ArrayList<>();

        for (int i = 0; i < participants.size(); i++) {
            GameParticipant p = participants.get(i);
            RoleDefinition role = roles.get(i);
            // 원숭이는 여기서 위장 직업이 정해지고 게임 끝까지 바뀌지 않는다.
            players.add(new GamePlayer(p.userId(), p.nickname(), role, roleAssigner.shownRoleOf(role)));
        }

        Game game = gameRepository.save(new Game(roomId, players, recordStats));
        gameFlowService.begin(game);

        synchronized (game) {
            return new StartGameResponse(game.getGameId(), game.getPhase(), game.getDay(), game.getPhaseEndsAt());
        }
    }

    /**
     * 3/5/6/9. 현재 페이즈와 공개 정보. 요청한 플레이어의 접속 시각도 기록한다.
     * 클라이언트가 게임 화면에서 1초마다 부르므로 연결 끊김 판정(InactivePlayerMonitor)의 기준이 된다.
     * 기록은 게임 잠금 밖에서 한다. (서버가 잠금 때문에 잠깐 느려진 것만으로 미접속 처리되지 않도록)
     */
    public GameStateResponse getState(String gameId, Long requesterId) {
        findGame(gameId).touch(requesterId, clock.instant());
        return getState(gameId);
    }

    /** 3/5/6/9. 현재 페이즈와 공개 정보 */
    public GameStateResponse getState(String gameId) {
        Game game = findGame(gameId);
        synchronized (game) {
            return GameStateResponse.from(game, clock.instant()); // phaseEndsAt과 같은 Clock 기준
        }
    }

    /** 2. 내 역할 조회 (본인만) */
    public MyRoleResponse getMyRole(String gameId, Long playerId) {
        Game game = findGame(gameId);
        game.touch(playerId, clock.instant());
        synchronized (game) {
            GamePlayer me = game.getPlayer(playerId);
            // isPirate()로 거르면 접선 전 앵무새가 드러나므로 접선 규칙이 들어간 knownPirateAllies를 쓴다.
            List<Long> teammates = game.knownPirateAllies(me).stream()
                    .map(GamePlayer::getPlayerId)
                    .toList();
            return MyRoleResponse.of(me, teammates);
        }
    }



    /** 8~9. 게임 결과. 종료 전에는 ended=false, 역할 비공개. */
    public GameResultResponse getResult(String gameId) {
        Game game = findGame(gameId);
        synchronized (game) {
            if (!game.isEnded()) {
                return new GameResultResponse(false, null, null, game.getDay(), List.of());
            }
            List<GameResultResponse.PlayerResult> results = game.getPlayers().stream()
                    .map(GameResultResponse.PlayerResult::from)
                    .toList();
            return new GameResultResponse(true, game.getWinner(), game.getEndReason(), game.getDay(), results);
        }
    }

    /**
     * 방을 IN_GAME으로 붙잡고 있어야 하는 게임인지 확인한다.
     *  - 메모리에 있고 아직 끝나지 않은 게임
     *  - 취소된 게임: 결과 조회 시간이 지나면 방을 삭제하므로 그때까지 대기 상태로 되돌리지 않는다
     * 로비가 "IN_GAME인데 게임이 사라진 방"(서버 재시작 등)을 찾아 복구할 때 사용한다.
     */
    public boolean keepsRoomInGame(String gameId) {
        if (gameId == null) {
            return false;
        }
        return gameRepository.findById(gameId)
                .map(game -> {
                    synchronized (game) {
                        return !game.isEnded() || game.isCancelled();
                    }
                })
                .orElse(false);
    }

    /** 개발용(local 전용 API에서만 호출): 전원의 실제 직업과 보이는 직업. */
    public List<DevRoleView> getDevRoles(String gameId) {
        Game game = findGame(gameId);
        synchronized (game) {
            return game.getPlayers().stream().map(DevRoleView::from).toList();
        }
    }

    private Game findGame(String gameId) {
        return gameRepository.findById(gameId).orElseThrow(() -> new GameNotFoundException(gameId));
    }
}
