package com.WhoisntCitizen_server.game.service;

import com.WhoisntCitizen_server.common.config.GamePhaseProperties;
import com.WhoisntCitizen_server.common.exception.GameNotFoundException;
import com.WhoisntCitizen_server.common.exception.LockTimeoutException;
import com.WhoisntCitizen_server.common.event.PirateNoticeEvent;
import com.WhoisntCitizen_server.common.event.RoomNoticeEvent;
import com.WhoisntCitizen_server.game.entity.DeathCause;
import com.WhoisntCitizen_server.game.entity.Game;
import com.WhoisntCitizen_server.game.entity.GameEndReason;
import com.WhoisntCitizen_server.game.entity.GamePhase;
import com.WhoisntCitizen_server.game.entity.GamePlayer;
import com.WhoisntCitizen_server.game.entity.Winner;
import com.WhoisntCitizen_server.game.event.CancelledGameExpiredEvent;
import com.WhoisntCitizen_server.game.event.GameEndedEvent;
import com.WhoisntCitizen_server.game.event.PlayersDepartedEvent;
import com.WhoisntCitizen_server.game.activity.PlayerActivityTracker;
import com.WhoisntCitizen_server.game.lock.GameLock;
import com.WhoisntCitizen_server.game.lock.GameLockScope;
import com.WhoisntCitizen_server.game.repository.GameRepository;
import com.WhoisntCitizen_server.game.scheduling.DeferredEventPublisher;
import com.WhoisntCitizen_server.game.scheduling.GameTimeoutHandler;
import com.WhoisntCitizen_server.game.scheduling.GameTimer;
import com.WhoisntCitizen_server.jobs.domain.ActionCode;
import com.WhoisntCitizen_server.night.entity.NightResult;
import com.WhoisntCitizen_server.night.service.NightActionResolver;
import com.WhoisntCitizen_server.vote.entity.ExecutionResult;
import com.WhoisntCitizen_server.vote.service.VoteResolver;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;
import org.springframework.context.ApplicationEventPublisher;
import org.springframework.stereotype.Service;

import java.time.Clock;
import java.time.Duration;
import java.time.Instant;
import java.util.ArrayList;
import java.util.List;
import java.util.Map;
import java.util.Optional;
import java.util.stream.Collectors;

/**
 * 서버가 주도하는 페이즈 진행(게임 루프). 클라이언트가 직접 호출하는 API가 없다.
 *
 *   start ─▶ 3.NIGHT ─(시간 종료 or 전원 제출)─▶ 4.NIGHT_RESULT ─▶ 5.DAY ─▶ 6.VOTE
 *              ▲                                   │(승리 시 ENDED)              │
 *              │                                                                ▼
 *              └──────────── 9.반복 ◀── 8.승리 조건 검사 ◀── 7.EXECUTION(처형)
 *                                          └─▶ 9.ENDED
 *
 * 페이즈가 바뀔 때마다 phaseVersion이 올라가므로, 전원 제출로 페이즈가 일찍 넘어가면
 * 이전에 예약된 타이머 작업은 버전 불일치로 무시된다.
 *
 * 끝나지 않는 게임 방지:
 *  - 연결 끊김(checkInactivePlayers): 마지막 요청 후 inactiveTimeoutSeconds 동안 요청이 없으면 사망 처리 + 방에서 제외 → 승리 조건 검사.
 *    살아 있는 플레이어가 모두 끊기면(다음 검사 전에 끊길 사람 포함) 아무도 사망 처리하지 않고 게임을 취소한다.
 *  - 사망자 없는 날: maxDaysWithoutDeath일 연속 아무도 죽지 않으면 그날 투표 결과 직후 게임을 취소한다.
 *  - 페이즈 전환 중 예외(Error 포함): 게임을 취소한다. (그대로 두면 그 페이즈에 영원히 멈춘다)
 *  취소된 게임은 승리 팀이 없고 전적에 반영하지 않으며, 결과 조회 시간이 지나면 방(과 채팅)도 삭제된다.
 */
@Service
public class GameFlowService implements GameTimeoutHandler {

    private static final Logger log = LoggerFactory.getLogger(GameFlowService.class);

    /** 타이머가 게임 잠금을 잡지 못했을 때 다시 시도하기까지 기다리는 시간 */
    static final Duration LOCK_RETRY_DELAY = Duration.ofSeconds(1);

    private final GameRepository gameRepository;
    private final NightActionResolver nightActionResolver;
    private final VoteResolver voteResolver;
    private final WinConditionChecker winConditionChecker;
    private final GameTimer gameTimer;
    private final DeferredEventPublisher deferredEvents;
    private final GameLock gameLock;
    private final PlayerActivityTracker activityTracker;
    private final GamePhaseProperties phaseProps;
    private final Clock clock;
    private final ApplicationEventPublisher eventPublisher;

    public GameFlowService(GameRepository gameRepository,
                           NightActionResolver nightActionResolver,
                           VoteResolver voteResolver,
                           WinConditionChecker winConditionChecker,
                           GameTimer gameTimer,
                           DeferredEventPublisher deferredEvents,
                           GameLock gameLock,
                           PlayerActivityTracker activityTracker,
                           GamePhaseProperties phaseProps,
                           Clock clock,
                           ApplicationEventPublisher eventPublisher) {
        this.gameRepository = gameRepository;
        this.nightActionResolver = nightActionResolver;
        this.voteResolver = voteResolver;
        this.winConditionChecker = winConditionChecker;
        this.gameTimer = gameTimer;
        this.deferredEvents = deferredEvents;
        this.gameLock = gameLock;
        this.activityTracker = activityTracker;
        this.phaseProps = phaseProps;
        this.clock = clock;
        this.eventPublisher = eventPublisher;
    }

    /** 1. 게임 시작 직후 첫 밤으로 진입. 게임은 이미 저장소에 저장돼 있어야 한다. */
    public void begin(String gameId) {
        gameLock.runWithLock(gameId, () -> {
            Game game = findGame(gameId);
            activityTracker.markAllSeen(gameId,
                    game.getPlayers().stream().map(GamePlayer::getPlayerId).toList(), clock.instant());
            enterNight(game);
        });
    }

    // ---------- 3. 밤 ----------

    private void enterNight(Game game) {
        moveTo(game, GamePhase.NIGHT, phaseProps.nightSeconds());
    }

    /**
     * 3 → 4. 밤 능력 처리 후 결과 공개 페이즈로. 전원 제출 시 NightService가, 시간 종료 시 타이머가 호출.
     * NightService는 게임 잠금 안에서 불러 온 Game을 그대로 넘긴다. 잠금은 재진입이라 다시 잡아도 막히지 않는다.
     */
    public void resolveNight(Game game) {
        gameLock.runWithLock(game.getGameId(), () -> {
            try {
                doResolveNight(game);
            } catch (RuntimeException | Error e) {
                cancelAfterError(game, e);
            }
        });
    }

    private void doResolveNight(Game game) {
        game.requirePhase(GamePhase.NIGHT);
        NightResult result = nightActionResolver.resolve(game);
        game.setLastNightResult(result);
        if (!result.deaths().isEmpty()) {
            game.recordDeath();
        }
        log.info("[{}] day {} 밤 결과: deaths={}, saved={}", game.getGameId(), game.getDay(),
                result.deaths(), result.protectedByDoctor());

        // 마피아·크라켄의 처치로 승패가 갈릴 수 있으므로 밤 결과 직후에도 승리 조건을 검사한다.
        // 채팅 안내 순서: 끝나면 "밤 결과 → 승리", 이어지면 "날이 밝았습니다 → 밤 결과"
        String nightMessage = nightResultMessage(game, result);
        if (winConditionChecker.check(game).isPresent()) {
            announce(game, nightMessage);
            finishIfWinnerDecided(game);
        } else {
            moveTo(game, GamePhase.NIGHT_RESULT, phaseProps.nightResultSeconds());
            announce(game, nightMessage);
        }
    }

    // ---------- 5. 낮 / 6. 투표 ----------

    private void enterDay(Game game) {
        moveTo(game, GamePhase.DAY, phaseProps.daySeconds());
    }

    private void enterVote(Game game) {
        moveTo(game, GamePhase.VOTE, phaseProps.voteSeconds());
    }

    /**
     * 5. 낮 토론 넘기기. 넘긴 사실과 인원을 방 채팅으로 알리고, 살아 있는 전원이 넘기면 시간을 기다리지 않고 바로 6. 투표로.
     * 규칙 위반(낮이 아님, 사망자)은 GameRuleException으로 그대로 던진다. GameService가 호출한다.
     */
    public void skipDay(Game game, Long playerId) {
        // GameService가 잡은 잠금과 같으므로 재진입한다. 다른 경로에서 불러도 타이머·연결 끊김 검사와 겹치지 않게 직접 잠근다.
        gameLock.runWithLock(game.getGameId(), () -> {
            if (!game.skipDay(playerId)) {
                return; // 이미 넘겼다
            }
            gameRepository.save(game);
            announce(game, game.getPlayer(playerId).getNickname() + "님이 토론을 넘겼습니다. ("
                    + game.daySkipCount() + "/" + game.aliveCount() + ")");
            if (game.allDaySkipped()) {
                try {
                    announce(game, "모두 토론을 넘겨 바로 투표를 시작합니다.");
                    enterVote(game);
                } catch (RuntimeException e) {
                    cancelAfterError(game, e);
                }
            }
        });
    }

    // ---------- 7. 처형 → 8. 승리 조건 검사 → 9. 반복/종료 ----------

    /**
     * 6 → 7 → 8 → 9. 전원 투표 시 VoteService가, 시간 종료 시 타이머가 호출.
     * VoteService는 게임 잠금 안에서 불러 온 Game을 그대로 넘긴다. (잠금 재진입)
     */
    public void resolveVote(Game game) {
        gameLock.runWithLock(game.getGameId(), () -> {
            try {
                doResolveVote(game);
            } catch (RuntimeException | Error e) {
                cancelAfterError(game, e);
            }
        });
    }

    private void doResolveVote(Game game) {
        game.requirePhase(GamePhase.VOTE);
        ExecutionResult result = voteResolver.resolve(game);       // 7. 처형 처리
        game.setLastExecutionResult(result);
        if (result.executedPlayerId() != null) {
            game.recordDeath();
        }
        log.info("[{}] day {} 처형 결과: executed={}, tie={}", game.getGameId(), game.getDay(),
                result.executedPlayerId(), result.tie());

        // 채팅 안내 순서: 끝나면 "처형 결과 → 승리", 이어지면 "투표가 끝났습니다 → 처형 결과"
        String executionMessage = executionResultMessage(game, result);
        if (winConditionChecker.check(game).isPresent()) {        // 8. 승리 조건 검사
            announce(game, executionMessage);
            finishIfWinnerDecided(game);
        } else if (tooLongWithoutDeath(game)) {
            // 정해진 일수 동안 아무도 죽지 않았다: 처형 결과를 알린 뒤 게임을 취소한다.
            announce(game, executionMessage);
            cancel(game, GameEndReason.CANCELLED_NO_DEATHS);
        } else {
            // 처형 결과를 잠깐 보여준 뒤 타이머 종료 시 다음 밤으로 (9. 반복)
            moveTo(game, GamePhase.EXECUTION, phaseProps.executionSeconds());
            announce(game, executionMessage);
        }
    }

    /** 8~9. 이긴 쪽이 정해졌으면 게임을 종료한다. */
    private boolean finishIfWinnerDecided(Game game) {
        Optional<WinConditionChecker.Victory> victory = winConditionChecker.check(game);
        if (victory.isEmpty()) {
            return false;
        }
        Winner winner = victory.get().winner();
        game.end(winner, victory.get().winnerIds());
        gameRepository.save(game);
        log.info("[{}] 게임 종료. 승리: {} {}", game.getGameId(), winner, victory.get().winnerIds());
        announce(game, winMessage(winner));
        publishLater(GameEndedEvent.from(game)); // 잠금 안에서 현재 상태를 복사해 둔다
        scheduleCleanup(game);
        return true;
    }

    private boolean tooLongWithoutDeath(Game game) {
        int maxDays = phaseProps.maxDaysWithoutDeath();
        return maxDays > 0 && game.daysWithoutDeath() >= maxDays;
    }

    // ---------- 게임 취소 ----------

    /**
     * 승리 팀 없이 게임을 끝낸다. 전적에는 반영하지 않고(UserStatsListener),
     * 결과 조회 시간이 끝나 게임을 메모리에서 지울 때 방도 삭제한다(CancelledGameExpiredEvent).
     * 반드시 게임 잠금(GameLock) 안에서 호출한다.
     */
    private void cancel(Game game, GameEndReason reason) {
        if (game.isEnded()) {
            return;
        }
        game.cancel(reason);
        gameRepository.save(game);
        log.warn("[{}] 게임 취소: {} (day {})", game.getGameId(), reason, game.getDay());
        announce(game, cancelMessage(reason));
        publishLater(GameEndedEvent.from(game));
        scheduleCleanup(game);
    }

    /**
     * 페이즈 전환 중 예외: 그대로 두면 그 페이즈에 영원히 멈추므로 게임을 취소한다. 취소가 실패해도 예외를 밖으로 던지지 않는다.
     * 판정 코드의 버그로 나는 Error(StackOverflowError 등)도 같은 이유로 여기서 처리한다.
     */
    private void cancelAfterError(Game game, Throwable e) {
        log.error("[{}] 페이즈 전환 실패 (phase={}) → 게임 취소", game.getGameId(), game.getPhase(), e);
        try {
            cancel(game, GameEndReason.CANCELLED_ERROR);
        } catch (RuntimeException | Error cancelError) {
            log.error("[{}] 게임 취소 처리 실패", game.getGameId(), cancelError);
        }
    }

    // ---------- 연결 끊김 ----------

    /**
     * 연결이 끊긴 플레이어 처리. InactivePlayerMonitor가 주기적으로 호출한다.
     * 마지막 요청 후 inactiveTimeoutSeconds 동안 요청이 없는 플레이어를 게임에서 내보낸다.
     *  - 살아 있는 플레이어가 모두 끊겼으면: 아무도 사망 처리하지 않고 게임을 취소한다.
     *    다음 검사(inactiveCheckSeconds 뒤) 전에 기준을 넘을 사람도 끊긴 것으로 센다. 함께 끊겨도 마지막 요청 시각이
     *    조금씩 달라 서로 다른 검사에 걸리면, 먼저 걸린 사람만 죽고 그 죽음으로 승패가 나 버리기 때문이다.
     *  - 아니면: 끊긴 사람을 한꺼번에 사망 처리하고 방에서 뺀 뒤 승리 조건을 한 번만 검사한다.
     *    (한 명씩 처리하면 동시에 끊긴 마지막 생존자들 사이에서 승패가 나 버린다)
     *    이미 죽은 사람(관전 중)이 끊기면 방에서만 뺀다.
     */
    public void checkInactivePlayers(String gameId) {
        if (phaseProps.inactiveTimeoutSeconds() <= 0) {
            return;
        }
        gameLock.runWithLock(gameId, () -> {
            Optional<Game> found = gameRepository.findById(gameId);
            if (found.isEmpty()) {
                return;
            }
            Game game = found.get();
            if (game.getPhase() == null || game.isEnded()) {
                return;
            }
            Instant cutoff = clock.instant().minusSeconds(phaseProps.inactiveTimeoutSeconds());
            // 같은 접속 기록 스냅샷으로 이번 기준과 다음 검사 기준을 함께 판단한다
            Map<Long, Instant> lastSeen = activityTracker.lastSeen(gameId);
            List<GamePlayer> inactive = game.inactivePlayers(lastSeen, cutoff);
            if (inactive.isEmpty()) {
                return;
            }
            // 다음 검사 때의 기준. 이번에는 내보내지 않지만 전원 끊김 판단에는 넣는다.
            Instant nextCutoff = cutoff.plusSeconds(Math.max(0, phaseProps.inactiveCheckSeconds()));
            long aliveGoneByNextCheck = game.inactivePlayers(lastSeen, nextCutoff).stream().filter(GamePlayer::isAlive).count();
            try {
                handleDepartures(game, inactive, aliveGoneByNextCheck);
            } catch (RuntimeException | Error e) {
                cancelAfterError(game, e);
            }
        });
    }

    private void handleDepartures(Game game, List<GamePlayer> inactive, long aliveGoneByNextCheck) {
        long inactiveAlive = inactive.stream().filter(GamePlayer::isAlive).count();
        if (inactiveAlive > 0 && aliveGoneByNextCheck == game.aliveCount()) {
            cancel(game, GameEndReason.CANCELLED_ALL_DISCONNECTED);
            return;
        }

        List<Long> departedIds = new ArrayList<>();
        boolean anyDied = false;
        for (GamePlayer p : inactive) {
            boolean wasAttackTarget = game.getPhase() == GamePhase.NIGHT && isAttackTarget(game, p.getPlayerId());
            boolean died = game.depart(p.getPlayerId());
            departedIds.add(p.getPlayerId());
            log.info("[{}] 연결 끊김: {}({}) → {}", game.getGameId(), p.getNickname(), p.getPlayerId(),
                    died ? "사망 처리, 방에서 제외" : "방에서 제외");
            if (died) {
                anyDied = true;
                announce(game, p.getNickname() + "님의 연결이 끊겨 사망 처리되었습니다.");
            }
            if (wasAttackTarget) {
                announceToPirates(game, p.getNickname() + "님이 사라져 공격 대상을 다시 골라야 합니다.");
            }
        }
        gameRepository.save(game);
        publishLater(new PlayersDepartedEvent(game.getGameId(), game.getRoomId(), List.copyOf(departedIds)));

        if (!anyDied || finishIfWinnerDecided(game)) {
            return;
        }
        // 떠난 사람의 표·행동을 지운 뒤 남은 사람이 모두 냈으면 타이머를 기다리지 않고 판정한다.
        if (game.getPhase() == GamePhase.NIGHT && game.allNightActionsSubmitted()) {
            doResolveNight(game);
        } else if (game.getPhase() == GamePhase.VOTE && game.allVotesSubmitted()) {
            doResolveVote(game);
        } else if (game.allDaySkipped()) {
            enterVote(game); // 남은 사람이 모두 낮 토론을 넘겼다
        }
    }

    private static boolean isAttackTarget(Game game, Long playerId) {
        return game.getNightActions().values().stream()
                .anyMatch(a -> a.code() == ActionCode.SELECT_ATTACK_TARGET && playerId.equals(a.targetId()));
    }

    /**
     * 10. 끝난 게임 정리. 결과를 조회할 시간(endedRetentionSeconds)을 준 뒤 메모리에서 삭제한다.
     * 삭제 후에는 해당 gameId로 조회하면 404(GAME_NOT_FOUND)가 된다.
     * 이미 예약된 페이즈 타이머가 늦게 실행돼도 onPhaseTimeout이 게임을 못 찾으면 그냥 끝나므로 안전하다.
     * 취소된 게임이면 이때 방도 삭제하도록 알린다. (취소 직후에 지우면 클라이언트가 취소 안내와 결과를 보지 못한다)
     * 방 삭제를 게임 삭제보다 먼저 한다. 게임이 먼저 사라지면 그사이 방 조회가 방을 대기 상태로 되돌려 삭제되지 않는다.
     */
    private void scheduleCleanup(Game game) {
        Instant at = clock.instant().plusSeconds(phaseProps.endedRetentionSeconds());
        gameTimer.scheduleCleanup(game.getGameId(), at);
    }

    /** 결과 조회 시간이 끝났을 때 타이머가 호출한다. (scheduleCleanup 참고) */
    @Override
    public void onCleanup(String gameId) {
        CleanupTarget target;
        try {
            target = gameLock.withLock(gameId, () -> gameRepository.findById(gameId)
                    .map(game -> new CleanupTarget(game.getRoomId(), game.isCancelled()))
                    .orElse(null));
        } catch (LockTimeoutException e) {
            // 정리를 건너뛰면 게임과 (취소된 게임이면) 방이 지워지지 않으므로 조금 뒤 다시 시도한다
            log.warn("[{}] 종료 게임 정리가 게임 잠금을 잡지 못해 {}초 뒤 다시 시도합니다", gameId, LOCK_RETRY_DELAY.toSeconds());
            gameTimer.scheduleCleanup(gameId, clock.instant().plus(LOCK_RETRY_DELAY));
            return;
        }
        if (target == null) {
            return;
        }
        // 방 삭제 이벤트는 게임 잠금 밖에서 발행한다. (로비가 방 잠금을 잡는다)
        if (target.cancelled()) {
            publishNow(new CancelledGameExpiredEvent(gameId, target.roomId()));
        }
        gameRepository.delete(gameId);
        activityTracker.clear(gameId);
        log.info("[{}] 종료된 게임을 메모리에서 삭제", gameId);
    }

    /** 정리할 때 필요한 값. 잠금 안에서 읽어 잠금 밖으로 가지고 나온다. */
    private record CleanupTarget(String roomId, boolean cancelled) {
    }

    /**
     * 로비·회원 모듈이 받는 이벤트(게임 종료, 연결 끊김) 발행.
     * 이 메서드는 게임 잠금(GameLock) 안에서 호출되므로, 이벤트 처리(로비 방 잠금, DB 저장)를
     * 게임 잠금을 쥔 채로 실행하지 않도록 DeferredEventPublisher로 잠금이 풀린 뒤 발행한다.
     * (게임 잠금 → 방 잠금 / 방 잠금 → 게임 잠금이 엇갈리며 생길 수 있는 교착 상태 방지)
     * 리스너에서 예외가 나도 게임 진행 스레드에는 영향이 없다.
     * event는 잠금 안에서 만든 값(현재 상태의 복사본)이어야 한다.
     */
    private void publishLater(Object event) {
        deferredEvents.publishAfterLock(event);
    }

    private void publishNow(Object event) {
        try {
            eventPublisher.publishEvent(event);
        } catch (RuntimeException e) {
            log.error("이벤트 처리 실패: {}", event, e);
        }
    }

    // ---------- 타이머 ----------
    //페이즈 즉시 설정 후 타임 아웃 등록
    private void moveTo(Game game, GamePhase next, int seconds) {
        Instant endsAt = clock.instant().plusSeconds(seconds);
        game.changePhase(next, endsAt);
        gameRepository.save(game);
        scheduleTimeout(game.getGameId(), game.getPhaseVersion(), endsAt);
        log.info("[{}] -> {} (day {}, v{})", game.getGameId(), next, game.getDay(), game.getPhaseVersion());
        announce(game, phaseMessage(game, next, seconds));
    }

    //페이즈 스케줄 등록
    private void scheduleTimeout(String gameId, long version, Instant at) {
        gameTimer.schedulePhaseTimeout(gameId, version, at);
    }

    //페이즈 시간 초과 시 호출
    /** 페이즈 제한 시간이 끝났을 때 호출된다. 이미 다음 페이즈로 넘어갔으면(버전 불일치) 무시. */
    @Override
    public void onPhaseTimeout(String gameId, long expectedVersion) {
        try {
            gameLock.runWithLock(gameId, () -> {
                Optional<Game> found = gameRepository.findById(gameId);
                if (found.isEmpty()) {
                    return;
                }
                Game game = found.get();
                if (game.getPhaseVersion() != expectedVersion || game.isEnded()) {
                    return;
                }
                try {
                    log.info("[{}] {} 종료", game.getGameId(), game.getPhase());
                    // 전원이 제출하면 일찍 끝나는 밤/투표만 시간 초과를 알린다. (나머지 페이즈는 항상 시간으로 넘어감)
                    if (game.getPhase() == GamePhase.NIGHT || game.getPhase() == GamePhase.VOTE) {
                        announce(game, "시간이 다 되어 다음 단계로 넘어갑니다.");
                    }
                    switch (game.getPhase()) {
                        case NIGHT -> doResolveNight(game);
                        case NIGHT_RESULT -> enterDay(game);
                        case DAY -> enterVote(game);
                        case VOTE -> doResolveVote(game);
                        case EXECUTION -> enterNight(game);
                        default -> { }
                    }
                } catch (RuntimeException | Error e) {
                    cancelAfterError(game, e);
                }
            });
        } catch (LockTimeoutException e) {
            // 잠금을 못 잡았으니 아무것도 바뀌지 않았다. 그냥 끝내면 게임이 이 페이즈에 멈추므로 조금 뒤 다시 시도한다.
            // 그사이 다른 경로(전원 제출 등)로 페이즈가 넘어갔으면 다시 시도했을 때 버전이 달라 무시된다.
            log.warn("[{}] 페이즈 타이머(v{})가 게임 잠금을 잡지 못해 {}초 뒤 다시 시도합니다",
                    gameId, expectedVersion, LOCK_RETRY_DELAY.toSeconds());
            gameTimer.schedulePhaseTimeout(gameId, expectedVersion, clock.instant().plus(LOCK_RETRY_DELAY));
        }
    }

    private Game findGame(String gameId) {
        return gameRepository.findById(gameId).orElseThrow(() -> new GameNotFoundException(gameId));
    }

    // ---------- 채팅 안내 (방 채팅창 시스템 메시지) ----------

    /**
     * 방 채팅창에 시스템 메시지로 남길 안내 문장을 발행한다. 채팅 모듈(ChatNoticeEventListener)이 받아 저장한다.
     * 게임 잠금 안에서 불려도 실제 발행(채팅 저장, 이후 WebSocket 전송)은 잠금이 풀린 직후 같은 스레드에서 한다.
     * (GameLockScope.afterUnlock: 넣은 순서대로, API 응답 전에 실행된다) 안내 문장은 잠금 안에서 미리 만들어 둔다.
     * 안내가 실패해도 게임 진행은 계속되도록 예외를 삼킨다.
     */
    private void announce(Game game, String message) {
        if (message == null) {
            return;
        }
        String gameId = game.getGameId();
        RoomNoticeEvent event = new RoomNoticeEvent(game.getRoomId(), message);
        GameLockScope.afterUnlock(() -> {
            try {
                eventPublisher.publishEvent(event);
            } catch (RuntimeException e) {
                log.warn("[{}] 채팅 안내 발행 실패: {}", gameId, e.getMessage());
            }
        });
    }

    /**
     * 같은 게임의 해적(접선한 앵무새 포함)에게만 보이는 시스템 메시지로 남길 안내를 발행한다.
     * (해적의 공격 대상 선택·넘기기, 앵무새 접선. NightService가 호출) 안내가 실패해도 게임 진행은 계속된다.
     * announce와 같이 게임 잠금이 풀린 뒤에 발행한다.
     */
    public void announceToPirates(Game game, String message) {
        if (message == null) {
            return;
        }
        String gameId = game.getGameId();
        PirateNoticeEvent event = new PirateNoticeEvent(game.getRoomId(), gameId, message);
        GameLockScope.afterUnlock(() -> {
            try {
                eventPublisher.publishEvent(event);
            } catch (RuntimeException e) {
                log.warn("[{}] 해적 안내 발행 실패: {}", gameId, e.getMessage());
            }
        });
    }

    private static String phaseMessage(Game game, GamePhase phase, int seconds) {
        return switch (phase) {
            case NIGHT -> game.getDay() + "일차 밤이 되었습니다. " + seconds + "초 동안 능력을 사용해 주세요.";
            case NIGHT_RESULT -> "날이 밝았습니다. 지난밤의 결과를 확인해 주세요.";
            case DAY -> "낮이 되었습니다. " + seconds + "초 동안 자유롭게 토론해 주세요.";
            case VOTE -> "투표 시간입니다. " + seconds + "초 안에 처형할 사람을 선택해 주세요.";
            case EXECUTION -> "투표가 끝났습니다. 처형 결과를 확인해 주세요.";
            default -> null;
        };
    }

    /** 죽은 사람마다 한 문장(해적 습격 먼저, 크라켄 다음). 유령 선장이 습격당한 밤은 아무 일 없던 것처럼 알린다. */
    private static String nightResultMessage(Game game, NightResult result) {
        if (!result.deaths().isEmpty()) {
            return result.deaths().stream()
                    .map(d -> "지난밤 " + nicknameOf(game, d.playerId()) + (d.cause() == DeathCause.KRAKEN
                            ? "님이 크라켄에게 끌려가 사망했습니다."
                            : "님이 해적의 습격을 받아 사망했습니다."))
                    .collect(Collectors.joining(" "));
        }
        if (result.protectedByDoctor()) {
            return "지난밤 습격이 있었지만, 선의의 보호로 아무도 죽지 않았습니다.";
        }
        return "지난밤은 아무 일도 일어나지 않았습니다.";
    }

    private static String executionResultMessage(Game game, ExecutionResult result) {
        if (result.executedPlayerId() != null) {
            return "투표 결과 " + nicknameOf(game, result.executedPlayerId()) + "님이 처형되었습니다.";
        }
        if (result.tie()) {
            return "투표가 동률로 끝나 아무도 처형되지 않았습니다.";
        }
        return "아무도 투표하지 않아 처형이 진행되지 않았습니다.";
    }

    private String cancelMessage(GameEndReason reason) {
        String why = switch (reason) {
            case CANCELLED_ALL_DISCONNECTED -> "살아 있는 플레이어가 모두 연결이 끊겨 게임이 취소되었습니다.";
            case CANCELLED_NO_DEATHS -> phaseProps.maxDaysWithoutDeath() + "일 동안 아무도 죽지 않아 게임이 취소되었습니다.";
            default -> "서버 오류로 게임이 취소되었습니다.";
        };
        return why + " 이번 게임은 전적에 반영되지 않으며, 잠시 후 방이 사라집니다.";
    }

    private static String winMessage(Winner winner) {
        return switch (winner) {
            case CREW -> "모든 해적이 사라졌습니다. 선원 팀이 승리했습니다!";
            case PIRATE -> "해적이 배를 장악했습니다. 해적 팀이 승리했습니다!";
            case SIREN -> "세이렌의 노래가 배를 집어삼켰습니다. 세이렌 팀이 승리했습니다!";
            case KRAKEN -> "크라켄이 배를 바다 밑으로 끌고 갔습니다. 크라켄이 승리했습니다!";
            case GHOST_CAPTAIN -> "죽은 자가 산 자보다 많아졌습니다. 유령 선장이 승리했습니다!";
            case MERMAID -> "처형된 사람은 인어였습니다. 인어가 승리했습니다!";
        };
    }

    private static String nicknameOf(Game game, Long playerId) {
        return game.getPlayer(playerId).getNickname();
    }
}
