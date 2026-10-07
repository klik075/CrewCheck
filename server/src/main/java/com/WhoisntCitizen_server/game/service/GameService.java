package com.WhoisntCitizen_server.game.service;

import com.WhoisntCitizen_server.common.exception.GameNotFoundException;
import com.WhoisntCitizen_server.common.exception.GameRuleException;
import com.WhoisntCitizen_server.game.dto.DevRoleView;
import com.WhoisntCitizen_server.game.dto.GameParticipant;
import com.WhoisntCitizen_server.game.dto.GameResultResponse;
import com.WhoisntCitizen_server.game.dto.GameStateResponse;
import com.WhoisntCitizen_server.game.dto.MyRoleResponse;
import com.WhoisntCitizen_server.game.dto.RoleSetup;
import com.WhoisntCitizen_server.game.dto.StartGameRequest;
import com.WhoisntCitizen_server.game.dto.StartGameResponse;
import com.WhoisntCitizen_server.game.activity.PlayerActivityTracker;
import com.WhoisntCitizen_server.game.entity.Game;
import com.WhoisntCitizen_server.game.entity.GamePlayer;
import com.WhoisntCitizen_server.game.lock.GameLock;
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
    private final GameLock gameLock;
    private final PlayerActivityTracker activityTracker;
    private final Clock clock;

    public GameService(GameRepository gameRepository, RoleAssigner roleAssigner, GameFlowService gameFlowService,
                       GameLock gameLock, PlayerActivityTracker activityTracker, Clock clock) {
        this.gameRepository = gameRepository;
        this.roleAssigner = roleAssigner;
        this.gameFlowService = gameFlowService;
        this.gameLock = gameLock;
        this.activityTracker = activityTracker;
        this.clock = clock;
    }

    /**
     * 1. 게임 시작 (Postman 테스트용 API: POST /api/v1/games).
     * 요청 DTO를 GameParticipant 목록으로 바꿔 서버 내부용 startGame에 위임한다.
     * roleSetup을 주면 방 설정과 같은 규칙으로 검증한 뒤 그대로 배정한다. (없으면 추천 구성)
     */
    public StartGameResponse startGame(StartGameRequest request) {
        List<GameParticipant> participants = request.players().stream()
                .map(e -> new GameParticipant(e.playerId(), e.nickname()))
                .toList();
        RoleSetup roleSetup = request.roleSetup() == null ? null : roleAssigner.normalize(request.roleSetup());
        // 개발용 게임: 가짜 playerId가 실제 User.id와 겹칠 수 있으므로 전적에 반영하지 않는다.
        return start(request.roomId(), participants, roleSetup, false);
    }

    /**
     * 1. 게임 시작 + 2. 역할 배정 → 첫 밤 진입 (서버 내부용). 추천 구성으로 배정한다.
     * 로비의 방 시작 처리(RoomService)가 Room의 참가자 목록으로 호출한다.
     * 인원 수(4~12명)는 RoleAssigner가 검사한다.
     */
    public StartGameResponse startGame(String roomId, List<GameParticipant> participants) {
        return startGame(roomId, participants, null);
    }

    /**
     * 1. 게임 시작 + 2. 역할 배정 → 첫 밤 진입 (서버 내부용). 방의 직업 배정 설정으로 배정한다.
     * 로비가 방에 RoleSetup을 저장하게 되면 위 메서드 대신 이것을 호출한다. roleSetup이 null이면 추천 구성.
     * 설정은 방장이 저장할 때 RoleAssigner.normalize로 검증된 값이어야 한다. 게임 시작 때 다시 확인해서
     * 지금 규칙에 맞지 않으면(서버 재시작 후 직업이 빠진 경우 등) GameRuleException(409)이 난다.
     */
    public StartGameResponse startGame(String roomId, List<GameParticipant> participants, RoleSetup roleSetup) {
        return start(roomId, participants, roleSetup, true);
    }

    private StartGameResponse start(String roomId, List<GameParticipant> participants, RoleSetup roleSetup,
                                    boolean recordStats) {
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

        List<RoleDefinition> roles = roleAssigner.assign(participants.size(), roleSetup);
        List<GamePlayer> players = new ArrayList<>();

        for (int i = 0; i < participants.size(); i++) {
            GameParticipant p = participants.get(i);
            RoleDefinition role = roles.get(i);
            // 원숭이는 여기서 이번 구성 안의 위장 직업이 정해지고 게임 끝까지 바뀌지 않는다.
            players.add(new GamePlayer(p.userId(), p.nickname(), role, roleAssigner.shownRoleOf(role, roles)));
        }

        String gameId = gameRepository.save(new Game(roomId, players, recordStats)).getGameId();
        gameFlowService.begin(gameId);

        return gameLock.withLock(gameId, () -> {
            Game game = findGame(gameId);
            return new StartGameResponse(game.getGameId(), game.getPhase(), game.getDay(), game.getPhaseEndsAt());
        });
    }

    /**
     * 3/5/6/9. 현재 페이즈와 공개 정보. 요청한 플레이어의 접속 시각도 기록한다.
     * 클라이언트가 게임 화면에서 1초마다 부르므로 연결 끊김 판정(InactivePlayerMonitor)의 기준이 된다.
     * 기록은 게임 잠금 밖에서 한다. (서버가 잠금 때문에 잠깐 느려진 것만으로 미접속 처리되지 않도록)
     */
    public GameStateResponse getState(String gameId, Long requesterId) {
        touch(gameId, requesterId);
        return getState(gameId);
    }

    /** 3/5/6/9. 현재 페이즈와 공개 정보 */
    public GameStateResponse getState(String gameId) {
        return gameLock.withLock(gameId, () ->
                GameStateResponse.from(findGame(gameId), clock.instant())); // phaseEndsAt과 같은 Clock 기준
    }

    /** 2. 내 역할 조회 (본인만) */
    public MyRoleResponse getMyRole(String gameId, Long playerId) {
        touch(gameId, playerId);
        return gameLock.withLock(gameId, () -> {
            Game game = findGame(gameId);
            return MyRoleResponse.of(game, game.getPlayer(playerId));
        });
    }

    /**
     * 접속 기록. 게임 잠금 밖에서 남긴다. 게임이 없으면 404, 참가자가 아니면 기록하지 않는다.
     * (참가자 명단은 바뀌지 않으므로 잠금 없이 확인해도 된다)
     */
    private void touch(String gameId, Long playerId) {
        if (findGame(gameId).hasPlayer(playerId)) {
            activityTracker.touch(gameId, playerId, clock.instant());
        }
    }



    /** 8~9. 게임 결과. 종료 전에는 ended=false, 역할 비공개. */
    public GameResultResponse getResult(String gameId) {
        return gameLock.withLock(gameId, () -> {
            Game game = findGame(gameId);
            if (!game.isEnded()) {
                return new GameResultResponse(false, null, null, game.getDay(), List.of());
            }
            List<GameResultResponse.PlayerResult> results = game.getPlayers().stream()
                    .map(p -> GameResultResponse.PlayerResult.from(p, game.getWinnerIds()))
                    .toList();
            return new GameResultResponse(true, game.getWinner(), game.getEndReason(), game.getDay(), results,
                    game.getWinnerIds());
        });
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
        return gameLock.withLock(gameId, () -> gameRepository.findById(gameId)
                .map(game -> !game.isEnded() || game.isCancelled())
                .orElse(false));
    }

    /** 개발용(local 전용 API에서만 호출): 전원의 실제 직업과 보이는 직업. */
    public List<DevRoleView> getDevRoles(String gameId) {
        return gameLock.withLock(gameId, () ->
                findGame(gameId).getPlayers().stream().map(DevRoleView::from).toList());
    }

    private Game findGame(String gameId) {
        return gameRepository.findById(gameId).orElseThrow(() -> new GameNotFoundException(gameId));
    }
}
