package com.WhoisntCitizen_server.game.service;

import com.WhoisntCitizen_server.common.config.GamePhaseProperties;
import com.WhoisntCitizen_server.game.entity.Game;
import com.WhoisntCitizen_server.game.entity.GamePhase;
import com.WhoisntCitizen_server.game.event.GameEndedEvent;
import com.WhoisntCitizen_server.game.repository.GameRepository;
import com.WhoisntCitizen_server.jobs.domain.Faction;
import com.WhoisntCitizen_server.night.entity.NightResult;
import com.WhoisntCitizen_server.night.service.NightActionResolver;
import com.WhoisntCitizen_server.vote.entity.ExecutionResult;
import com.WhoisntCitizen_server.vote.service.VoteResolver;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;
import org.springframework.beans.factory.annotation.Qualifier;
import org.springframework.context.ApplicationEventPublisher;
import org.springframework.scheduling.TaskScheduler;
import org.springframework.stereotype.Service;

import java.time.Clock;
import java.time.Instant;
import java.util.Optional;

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
 */
@Service
public class GameFlowService {

    private static final Logger log = LoggerFactory.getLogger(GameFlowService.class);

    private final GameRepository gameRepository;
    private final NightActionResolver nightActionResolver;
    private final VoteResolver voteResolver;
    private final WinConditionChecker winConditionChecker;
    private final TaskScheduler scheduler;
    private final GamePhaseProperties phaseProps;
    private final Clock clock;
    private final ApplicationEventPublisher eventPublisher;

    public GameFlowService(GameRepository gameRepository,
                           NightActionResolver nightActionResolver,
                           VoteResolver voteResolver,
                           WinConditionChecker winConditionChecker,
                           @Qualifier("gamePhaseScheduler") TaskScheduler scheduler,
                           GamePhaseProperties phaseProps,
                           Clock clock,
                           ApplicationEventPublisher eventPublisher) {
        this.gameRepository = gameRepository;
        this.nightActionResolver = nightActionResolver;
        this.voteResolver = voteResolver;
        this.winConditionChecker = winConditionChecker;
        this.scheduler = scheduler;
        this.phaseProps = phaseProps;
        this.clock = clock;
        this.eventPublisher = eventPublisher;
    }

    /** 1. 게임 시작 직후 첫 밤으로 진입. */
    public void begin(Game game) {
        synchronized (game) {
            enterNight(game);
        }
    }

    // ---------- 3. 밤 ----------

    private void enterNight(Game game) {
        moveTo(game, GamePhase.NIGHT, phaseProps.nightSeconds());
    }

    /** 3 → 4. 밤 능력 처리 후 결과 공개 페이즈로. 전원 제출 시 NightService가, 시간 종료 시 타이머가 호출. */
    public void resolveNight(Game game) {
        synchronized (game) {
            doResolveNight(game);
        }
    }

    private void doResolveNight(Game game) {
        game.requirePhase(GamePhase.NIGHT);
        NightResult result = nightActionResolver.resolve(game);
        game.setLastNightResult(result);
        log.info("[{}] day {} 밤 결과: killed={}, saved={}", game.getGameId(), game.getDay(),
                result.killedPlayerId(), result.protectedByDoctor());

        // 마피아의 처치로 승패가 갈릴 수 있으므로 밤 결과 직후에도 승리 조건을 검사한다.
        if (!finishIfWinnerDecided(game)) {
            moveTo(game, GamePhase.NIGHT_RESULT, phaseProps.nightResultSeconds());
        }
    }

    // ---------- 5. 낮 / 6. 투표 ----------

    private void enterDay(Game game) {
        moveTo(game, GamePhase.DAY, phaseProps.daySeconds());
    }

    private void enterVote(Game game) {
        moveTo(game, GamePhase.VOTE, phaseProps.voteSeconds());
    }

    // ---------- 7. 처형 → 8. 승리 조건 검사 → 9. 반복/종료 ----------

    /** 6 → 7 → 8 → 9. 전원 투표 시 VoteService가, 시간 종료 시 타이머가 호출. */
    public void resolveVote(Game game) {
        synchronized (game) {
            doResolveVote(game);
        }
    }

    private void doResolveVote(Game game) {
        game.requirePhase(GamePhase.VOTE);
        ExecutionResult result = voteResolver.resolve(game);       // 7. 처형 처리
        game.setLastExecutionResult(result);
        log.info("[{}] day {} 처형 결과: executed={}, tie={}", game.getGameId(), game.getDay(),
                result.executedPlayerId(), result.tie());

        if (!finishIfWinnerDecided(game)) {                         // 8. 승리 조건 검사
            // 처형 결과를 잠깐 보여준 뒤 타이머 종료 시 다음 밤으로 (9. 반복)
            moveTo(game, GamePhase.EXECUTION, phaseProps.executionSeconds());
        }
    }

    /** 8~9. 승리 팀이 정해졌으면 게임을 종료한다. */
    private boolean finishIfWinnerDecided(Game game) {
        Optional<Faction> winner = winConditionChecker.check(game);
        if (winner.isEmpty()) {
            return false;
        }
        game.end(winner.get());
        gameRepository.save(game);
        log.info("[{}] 게임 종료. 승리: {}", game.getGameId(), winner.get());
        publishGameEnded(game);
        scheduleCleanup(game.getGameId());
        return true;
    }

    /**
     * 10. 끝난 게임 정리. 결과를 조회할 시간(endedRetentionSeconds)을 준 뒤 메모리에서 삭제한다.
     * 삭제 후에는 해당 gameId로 조회하면 404(GAME_NOT_FOUND)가 된다.
     * 이미 예약된 페이즈 타이머가 늦게 실행돼도 onPhaseTimeout이 게임을 못 찾으면 그냥 끝나므로 안전하다.
     */
    private void scheduleCleanup(String gameId) {
        Instant at = clock.instant().plusSeconds(phaseProps.endedRetentionSeconds());
        scheduler.schedule(() -> {
            gameRepository.delete(gameId);
            log.info("[{}] 종료된 게임을 메모리에서 삭제", gameId);
        }, at);
    }

    /**
     * 게임 종료 이벤트 발행.
     * 이 메서드는 synchronized(game) 안에서 호출되므로, 이벤트 처리(로비 방 잠금, DB 저장)를
     * 게임 잠금을 쥔 채로 실행하지 않도록 스케줄러 스레드에서 따로 발행한다.
     * (게임 잠금 → 방 잠금 / 방 잠금 → 게임 잠금이 엇갈리며 생길 수 있는 교착 상태 방지)
     * 리스너에서 예외가 나도 게임 진행 스레드에는 영향이 없다.
     */
    private void publishGameEnded(Game game) {
        GameEndedEvent event = GameEndedEvent.from(game); // 잠금 안에서 현재 상태를 복사해 둔다
        scheduler.schedule(() -> {
            try {
                eventPublisher.publishEvent(event);
            } catch (RuntimeException e) {
                log.error("[{}] 게임 종료 이벤트 처리 실패", event.gameId(), e);
            }
        }, clock.instant());
    }

    // ---------- 타이머 ----------
    //페이즈 즉시 설정 후 타임 아웃 등록
    private void moveTo(Game game, GamePhase next, int seconds) {
        Instant endsAt = clock.instant().plusSeconds(seconds);
        game.changePhase(next, endsAt);
        gameRepository.save(game);
        scheduleTimeout(game.getGameId(), game.getPhaseVersion(), endsAt);
        log.info("[{}] -> {} (day {}, v{})", game.getGameId(), next, game.getDay(), game.getPhaseVersion());
    }

    //페이즈 스케줄 등록
    private void scheduleTimeout(String gameId, long version, Instant at) {
        scheduler.schedule(() -> onPhaseTimeout(gameId, version), at);
    }

    //페이즈 시간 초과 시 호출
    /** 페이즈 제한 시간이 끝났을 때 호출된다. 이미 다음 페이즈로 넘어갔으면(버전 불일치) 무시. */
    void onPhaseTimeout(String gameId, long expectedVersion) {
        Optional<Game> found = gameRepository.findById(gameId);
        if (found.isEmpty()) {
            return;
        }
        Game game = found.get();
        synchronized (game) {
            if (game.getPhaseVersion() != expectedVersion || game.isEnded()) {
                return;
            }
            try {
                log.info("[{}] {} 종료", game.getGameId(), game.getPhase());
                switch (game.getPhase()) {
                    case NIGHT -> doResolveNight(game);
                    case NIGHT_RESULT -> enterDay(game);
                    case DAY -> enterVote(game);
                    case VOTE -> doResolveVote(game);
                    case EXECUTION -> enterNight(game);
                    default -> { }
                }
            } catch (RuntimeException e) {
                log.error("[{}] 페이즈 전환 실패 (phase={})", gameId, game.getPhase(), e);
            }
        }
    }
}
