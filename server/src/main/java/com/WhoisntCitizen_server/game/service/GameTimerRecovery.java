package com.WhoisntCitizen_server.game.service;

import com.WhoisntCitizen_server.game.entity.Game;
import com.WhoisntCitizen_server.game.lock.GameLock;
import com.WhoisntCitizen_server.game.repository.GameRepository;
import com.WhoisntCitizen_server.game.scheduling.GameTimer;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;
import org.springframework.boot.context.event.ApplicationReadyEvent;
import org.springframework.context.event.EventListener;
import org.springframework.stereotype.Component;

import java.time.Clock;
import java.time.Instant;
import java.util.Optional;

/**
 * 서버가 시작될 때 진행 중인 게임의 페이즈 타이머를 다시 건다.
 *
 * 게임 상태는 Redis에 남지만, 페이즈 타이머(LocalGameTimer)는 서버 메모리에 있어서 재시작하면 사라진다.
 * 그대로 두면 "21:00:30에 밤이 끝난다"는 상태만 남고 밤을 끝낼 주체가 없어 게임이 그 페이즈에 멈춘다.
 * 그래서 시작할 때 진행 중인 게임마다 저장된 phaseEndsAt에 맞춰 타이머를 다시 예약한다.
 *  - 아직 시간이 남았으면 그 시각에
 *  - 꺼져 있는 동안 이미 지났으면 바로 (다음 페이즈로 넘어간다)
 *
 * 같은 페이즈에 타이머가 두 번 걸려도 괜찮다. 타이머는 phaseVersion을 함께 넘기고,
 * onPhaseTimeout이 게임 잠금 안에서 버전을 확인해 이미 넘어간 페이즈면 무시한다.
 *
 * 다루지 않는 것
 *  - 끝난 게임의 정리 타이머: 끝난 게임은 진행 중 목록에 없다. Redis에서는 끝난 게임 키에 TTL(endedTtl)이 있어 결국 지워진다.
 *    (그 경우 취소된 게임의 방 삭제 이벤트는 발행되지 않고, 로비가 게임이 없는 방을 대기 상태로 되돌린다)
 *  - 메모리 저장소(InMemoryGameRepository): 재시작하면 게임 자체가 사라지므로 할 일이 없다.
 *  - 서버 여러 대 중 한 대만 죽은 경우: 살아 있는 서버는 재시작하지 않으므로 죽은 서버가 걸어 둔 타이머를 대신 걸지 않는다.
 *    이 문제는 타이머를 Redis로 옮기는 3단계에서 해결한다. 그때 이 클래스는 필요 없어지거나 줄어든다.
 */
@Component
public class GameTimerRecovery {

    private static final Logger log = LoggerFactory.getLogger(GameTimerRecovery.class);

    private final GameRepository gameRepository;
    private final GameLock gameLock;
    private final GameTimer gameTimer;
    private final Clock clock;

    public GameTimerRecovery(GameRepository gameRepository, GameLock gameLock, GameTimer gameTimer, Clock clock) {
        this.gameRepository = gameRepository;
        this.gameLock = gameLock;
        this.gameTimer = gameTimer;
        this.clock = clock;
    }

    @EventListener(ApplicationReadyEvent.class)
    public void onApplicationReady() {
        int recovered = recoverAll();
        if (recovered > 0) {
            log.info("진행 중인 게임 {}개의 페이즈 타이머를 다시 걸었습니다", recovered);
        }
    }

    /** 진행 중인 모든 게임의 타이머를 다시 건다. 다시 건 게임 수를 돌려준다. 한 게임이 실패해도 나머지는 계속한다. */
    public int recoverAll() {
        int recovered = 0;
        for (String gameId : gameRepository.findActiveIds()) {
            try {
                if (recover(gameId)) {
                    recovered++;
                }
            } catch (RuntimeException e) {
                log.error("[{}] 페이즈 타이머 복구 실패", gameId, e);
            }
        }
        return recovered;
    }

    /** 게임 하나의 타이머를 다시 건다. 다시 걸었으면 true. 게임은 잠금 안에서 다시 읽는다. */
    boolean recover(String gameId) {
        return gameLock.withLock(gameId, () -> {
            Optional<Game> found = gameRepository.findById(gameId);
            if (found.isEmpty()) {
                return false; // 목록을 받은 뒤 지워졌거나 TTL로 사라짐
            }
            Game game = found.get();
            if (game.isEnded()) {
                return false;
            }
            if (game.getPhase() == null || game.getPhaseEndsAt() == null) {
                // 시작 전에 서버가 꺼진 게임. 시작(begin)을 부를 주체가 없어 진행되지 않는다
                log.warn("[{}] 시작 전 상태라 타이머를 걸 수 없습니다", gameId);
                return false;
            }
            Instant now = clock.instant();
            Instant at = game.getPhaseEndsAt().isBefore(now) ? now : game.getPhaseEndsAt();
            gameTimer.schedulePhaseTimeout(gameId, game.getPhaseVersion(), at);
            log.info("[{}] {} 타이머 복구 (v{}, {})", gameId, game.getPhase(), game.getPhaseVersion(), at);
            return true;
        });
    }
}
