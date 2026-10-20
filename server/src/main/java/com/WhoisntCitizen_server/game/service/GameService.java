package com.WhoisntCitizen_server.game.service;

import com.WhoisntCitizen_server.common.exception.GameNotFoundException;
import com.WhoisntCitizen_server.common.exception.GameRuleException;
import com.WhoisntCitizen_server.game.dto.DayExtendResponse;
import com.WhoisntCitizen_server.game.dto.DaySkipResponse;
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
import com.WhoisntCitizen_server.game.lock.SnapshotRead;
import com.WhoisntCitizen_server.game.repository.GameRepository;
import com.WhoisntCitizen_server.jobs.domain.RoleDefinition;
import org.springframework.stereotype.Service;

import java.util.ArrayList;
import java.util.HashSet;
import java.util.List;
import java.util.Optional;
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
     * 인원 수(4~12명)는 RoleAssigner가 검사한다.
     */
    public StartGameResponse startGame(String roomId, List<GameParticipant> participants) {
        return startGame(roomId, participants, null);
    }

    /**
     * 1. 게임 시작 + 2. 역할 배정 → 첫 밤 진입 (서버 내부용). 방의 직업 배정 설정으로 배정한다.
     * 로비의 방 시작 처리(RoomService)가 Room의 참가자 목록과 저장된 설정으로 호출한다. roleSetup이 null이면 추천 구성.
     * 설정은 방장이 저장할 때 normalizeRoleSetup으로 검증된 값이어야 한다. 게임 시작 때 다시 확인해서
     * 지금 규칙에 맞지 않으면(서버 재시작 후 직업이 빠진 경우 등) GameRuleException(409)이 난다.
     */
    public StartGameResponse startGame(String roomId, List<GameParticipant> participants, RoleSetup roleSetup) {
        return start(roomId, participants, roleSetup, true);
    }

    /**
     * 방장이 보낸 직업 배정 설정을 검증하고 방에 저장할 형태로 정리한다. (로비의 PUT /rooms/{roomId}/role-setup)
     * 규칙에 맞지 않으면 IllegalArgumentException(400). 규칙은 RoleAssigner.normalize 참고.
     */
    public RoleSetup normalizeRoleSetup(RoleSetup setup) {
        return roleAssigner.normalize(setup);
    }

    /** 설정을 저장한 적 없는 방의 직업 배정 설정: 추천 구성, 랜덤 후보는 특수 직업 전부. */
    public RoleSetup defaultRoleSetup() {
        return roleAssigner.defaultSetup();
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
            players.add(new GamePlayer(p.userId(), p.nickname(), role, roleAssigner.shownRoleOf(role, roles), p.profileImageId()));
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

    /** 3/5/6/9. 현재 페이즈와 공개 정보. 보여 주기만 하므로 Redis 저장소에서는 잠금 없이 읽는다. (SnapshotRead) */
    public GameStateResponse getState(String gameId) {
        return SnapshotRead.of(gameRepository, gameLock, gameId, () ->
                GameStateResponse.from(findGame(gameId), clock.instant())); // phaseEndsAt과 같은 Clock 기준
    }

    /** 2. 내 역할 조회 (본인만). 보여 주기만 하므로 Redis 저장소에서는 잠금 없이 읽는다. (SnapshotRead) */
    public MyRoleResponse getMyRole(String gameId, Long playerId) {
        touch(gameId, playerId);
        return SnapshotRead.of(gameRepository, gameLock, gameId, () -> {
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



    /** 8~9. 게임 결과. 종료 전에는 ended=false, 역할 비공개. 보여 주기만 하므로 SnapshotRead로 읽는다. */
    public GameResultResponse getResult(String gameId) {
        return SnapshotRead.of(gameRepository, gameLock, gameId, () -> {
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
     *
     * 방 목록은 게임 중인 방마다 이 확인을 한다. 매번 게임 잠금을 잡으면 방 목록 한 번이 모든 게임의 잠금 줄에 차례로 서서,
     * 게임이 바쁠 때 로비까지 느려진다. (부하 테스트: 방 5개에서 방 목록 p99 1.85초)
     * 그래서 복사본을 돌려주는 저장소(Redis)에서는 먼저 잠금 없이 읽는다.
     *  - 살아 있는 게임이면(true) 그대로 끝낸다. 방을 IN_GAME으로 두는 것뿐이라 상태를 바꾸지 않는다.
     *    그사이 게임이 끝나도 다음 조회에서 다시 확인되고, 정상 종료는 GameEndedEvent가 방을 대기 상태로 돌린다.
     *  - 사라졌거나 끝난 것처럼 보이면(false) 호출한 쪽이 방을 대기 상태로 되돌리므로, 잠금 안에서 한 번 더 확인한다.
     */
    public boolean keepsRoomInGame(String gameId) {
        if (gameId == null) {
            return false;
        }
        if (gameRepository.returnsSnapshots() && keepsRoom(gameRepository.findById(gameId))) {
            return true;
        }
        return gameLock.withLock(gameId, () -> keepsRoom(gameRepository.findById(gameId)));
    }

    private static boolean keepsRoom(Optional<Game> game) {
        return game.map(g -> !g.isEnded() || g.isCancelled()).orElse(false);
    }

    /** 개발용(local 전용 API에서만 호출): 전원의 실제 직업과 보이는 직업. */
    public List<DevRoleView> getDevRoles(String gameId) {
        return gameLock.withLock(gameId, () ->
                findGame(gameId).getPlayers().stream().map(DevRoleView::from).toList());
    }

    /**
     * 5. 낮 토론 넘기기. 살아 있는 전원이 넘기면 바로 투표로 넘어간다(응답의 phase가 VOTE).
     * 다른 게임 API·타이머와 같은 GameLock으로 잠근다. (synchronized(game)은 GameLock과 다른 잠금이라 서로 막지 못한다)
     */
    public DaySkipResponse skipDay(String gameId, Long playerId) {
        return gameLock.withLock(gameId, () -> {
            Game game = findGame(gameId); // 잠금 안에서 불러온다 (GameLock 사용 규칙)
            gameFlowService.skipDay(game, playerId);
            return new DaySkipResponse(true, game.getPhase(), game.getPhaseVersion(),
                    game.daySkipCount(), game.aliveCount());
        });
    }

    /**
     * 5. 낮 토론 시간 연장. 살아 있는 사람마다 낮에 한 번 마감을 늦춘다. 이미 늘린 사람이 다시 보내면 시간은 그대로 두고 addedSeconds=0.
     * 페이즈가 바뀌지 않으므로 phaseVersion은 그대로이고, 늘어난 마감은 응답과 이후 상태 조회의 phaseEndsAt에 보인다.
     * skipDay와 같은 GameLock으로 잠근다.
     */
    public DayExtendResponse extendDay(String gameId, Long playerId) {
        return gameLock.withLock(gameId, () -> {
            Game game = findGame(gameId); // 잠금 안에서 불러온다 (GameLock 사용 규칙)
            long addedSeconds = gameFlowService.extendDay(game, playerId);
            return new DayExtendResponse(true, game.getPhase(), game.getPhaseVersion(), game.getPhaseEndsAt(),
                    addedSeconds, game.dayExtendCount());
        });
    }

    private Game findGame(String gameId) {
        return gameRepository.findById(gameId).orElseThrow(() -> new GameNotFoundException(gameId));
    }
}
