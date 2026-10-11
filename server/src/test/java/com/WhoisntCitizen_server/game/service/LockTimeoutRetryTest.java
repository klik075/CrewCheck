package com.WhoisntCitizen_server.game.service;

import com.WhoisntCitizen_server.common.config.GamePhaseProperties;
import com.WhoisntCitizen_server.common.exception.LockTimeoutException;
import com.WhoisntCitizen_server.game.activity.LocalPlayerActivityTracker;
import com.WhoisntCitizen_server.game.entity.Game;
import com.WhoisntCitizen_server.game.entity.GamePhase;
import com.WhoisntCitizen_server.game.entity.GamePlayer;
import com.WhoisntCitizen_server.game.lock.GameLock;
import com.WhoisntCitizen_server.game.lock.LocalGameLock;
import com.WhoisntCitizen_server.game.scheduling.LocalGameTimer;
import com.WhoisntCitizen_server.game.scheduling.SchedulerDeferredEventPublisher;
import com.WhoisntCitizen_server.night.service.NightActionResolver;
import com.WhoisntCitizen_server.support.CopyingGameRepository;
import com.WhoisntCitizen_server.support.ManualTaskScheduler;
import com.WhoisntCitizen_server.support.MutableClock;
import com.WhoisntCitizen_server.support.TestRoles;
import com.WhoisntCitizen_server.vote.service.VoteResolver;
import org.junit.jupiter.api.BeforeEach;
import org.junit.jupiter.api.Test;

import java.time.Duration;
import java.time.Instant;
import java.util.List;
import java.util.Random;
import java.util.concurrent.atomic.AtomicInteger;
import java.util.function.Supplier;

import static org.assertj.core.api.Assertions.assertThat;

/**
 * 2-5: 백그라운드 작업(페이즈 타이머, 종료 게임 정리, 타이머 복구)이 게임 잠금을 잡지 못했을 때
 * 그냥 끝나지 않고 다시 시도하는지 확인한다. (그냥 끝나면 게임이 그 페이즈에 멈춘다)
 *
 * FailingGameLock: 지정한 횟수만큼 LockTimeoutException을 던진 뒤 정상 잠금처럼 동작한다.
 * (다른 서버가 잠금을 오래 쥐고 있는 상황을 흉내 낸다)
 */
class LockTimeoutRetryTest {

    private static final Instant NOW = Instant.parse("2026-10-01T12:00:00Z");
    // 밤 30, 결과 5, 낮 60, 투표 30, 처형 5. 종료 후 60초 보관, 연결 끊김 검사 끔, 10일 무사망 시 취소
    private static final GamePhaseProperties PROPS = new GamePhaseProperties(30, 5, 60, 30, 5, 60, 0, 5, 10);

    /** 남은 실패 횟수만큼 LockTimeoutException을 던지고, 그다음부터는 정상 잠금 */
    private static final class FailingGameLock implements GameLock {
        private final LocalGameLock delegate = new LocalGameLock();
        private final AtomicInteger failuresLeft = new AtomicInteger();
        final AtomicInteger attempts = new AtomicInteger();

        void failNext(int times) {
            failuresLeft.set(times);
            attempts.set(0);
        }

        @Override
        public <T> T withLock(String gameId, Supplier<T> action) {
            attempts.incrementAndGet();
            if (failuresLeft.getAndUpdate(n -> Math.max(0, n - 1)) > 0) {
                throw new LockTimeoutException("게임", gameId, Duration.ofSeconds(10));
            }
            return delegate.withLock(gameId, action);
        }
    }

    private MutableClock clock;
    private ManualTaskScheduler scheduler;
    private CopyingGameRepository repository;
    private FailingGameLock lock;
    private GameFlowService flow;
    private String gameId;

    @BeforeEach
    void setUp() {
        clock = new MutableClock(NOW);
        scheduler = new ManualTaskScheduler(clock);
        repository = new CopyingGameRepository();
        lock = new FailingGameLock();
        GameFlowService[] holder = new GameFlowService[1];
        holder[0] = new GameFlowService(repository, new NightActionResolver(new Random(0)), new VoteResolver(),
                new WinConditionChecker(), new LocalGameTimer(scheduler, () -> holder[0]),
                new SchedulerDeferredEventPublisher(scheduler, clock, event -> { }),
                lock, new LocalPlayerActivityTracker(), PROPS, clock, event -> { });
        flow = holder[0];
        gameId = repository.save(new Game("1", List.of(
                new GamePlayer(1L, "해적", TestRoles.RAIDER),
                new GamePlayer(2L, "선장", TestRoles.CAPTAIN),
                new GamePlayer(3L, "선원3", TestRoles.SAILOR),
                new GamePlayer(4L, "선원4", TestRoles.SAILOR),
                new GamePlayer(5L, "선원5", TestRoles.SAILOR)), true)).getGameId();
    }

    private Game stored() {
        return repository.findById(gameId).orElseThrow();
    }

    private void advance(int seconds) {
        scheduler.advance(Duration.ofSeconds(seconds));
        scheduler.runDue();
    }

    // ---------- 페이즈 타이머 ----------

    @Test
    void 페이즈_타이머가_잠금을_못_잡으면_1초마다_다시_시도해_결국_넘어간다() {
        flow.begin(gameId);                 // 밤: 30초 뒤 끝남
        lock.failNext(2);                   // 다음 두 번은 잠금을 못 잡음

        advance(30);                        // 1번째 시도 실패 → 1초 뒤 다시
        assertThat(stored().getPhase()).isEqualTo(GamePhase.NIGHT);
        advance(1);                         // 2번째 시도 실패 → 1초 뒤 다시
        assertThat(stored().getPhase()).isEqualTo(GamePhase.NIGHT);
        advance(1);                         // 3번째 시도 성공

        assertThat(stored().getPhase()).isEqualTo(GamePhase.NIGHT_RESULT);
        assertThat(lock.attempts.get()).isGreaterThanOrEqualTo(3);
    }

    @Test
    void 다시_시도하기_전에_페이즈가_이미_넘어갔으면_다시_시도한_타이머는_무시된다() {
        flow.begin(gameId);
        long nightVersion = stored().getPhaseVersion();
        lock.failNext(1);

        advance(30);                        // 실패 → 1초 뒤 v(밤)로 다시 예약
        // 그사이 다른 경로로 페이즈가 넘어간 상황을 만든다 (예: 전원 제출)
        flow.onPhaseTimeout(gameId, nightVersion);
        assertThat(stored().getPhase()).isEqualTo(GamePhase.NIGHT_RESULT);
        long afterVersion = stored().getPhaseVersion();

        advance(1);                         // 밀린 v(밤) 타이머 실행 → 버전이 달라 무시

        assertThat(stored().getPhase()).isEqualTo(GamePhase.NIGHT_RESULT);
        assertThat(stored().getPhaseVersion()).isEqualTo(afterVersion);
    }

    // ---------- 종료 게임 정리 ----------

    @Test
    void 종료_게임_정리가_잠금을_못_잡으면_1초_뒤_다시_시도해_결국_지운다() {
        lock.failNext(1);

        flow.onCleanup(gameId);             // 실패 → 1초 뒤 다시
        assertThat(repository.findById(gameId)).isPresent();

        advance(1);                         // 다시 시도 성공

        assertThat(repository.findById(gameId)).isEmpty();
    }

    // ---------- 서버 시작 시 타이머 복구 ----------

    @Test
    void 타이머_복구가_잠금을_못_잡은_게임은_다시_시도해_복구한다() {
        flow.begin(gameId);
        ManualTaskScheduler restarted = new ManualTaskScheduler(clock);   // 재시작: 예전 타이머는 버림
        GameTimerRecovery recovery = new GameTimerRecovery(repository, lock,
                new LocalGameTimer(restarted, () -> flow), clock);
        lock.failNext(1);                   // 첫 시도만 실패

        assertThat(recovery.recoverAll()).isEqualTo(1);   // 두 번째 시도에서 복구

        restarted.advance(Duration.ofSeconds(30));
        restarted.runDue();
        assertThat(stored().getPhase()).isEqualTo(GamePhase.NIGHT_RESULT);
    }

    @Test
    void 타이머_복구는_정해진_횟수만_다시_시도하고_포기한다() {
        flow.begin(gameId);
        GameTimerRecovery recovery = new GameTimerRecovery(repository, lock,
                new LocalGameTimer(new ManualTaskScheduler(clock), () -> flow), clock);
        lock.failNext(100);                 // 계속 실패

        assertThat(recovery.recoverAll()).isZero();
        assertThat(lock.attempts.get()).isEqualTo(GameTimerRecovery.MAX_ROUNDS);
    }
}
