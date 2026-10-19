package com.WhoisntCitizen_server.game.service;

import com.WhoisntCitizen_server.common.config.GamePhaseProperties;
import com.WhoisntCitizen_server.game.activity.LocalPlayerActivityTracker;
import com.WhoisntCitizen_server.game.entity.Game;
import com.WhoisntCitizen_server.game.entity.GamePlayer;
import com.WhoisntCitizen_server.game.lock.GameLock;
import com.WhoisntCitizen_server.game.lock.LocalGameLock;
import com.WhoisntCitizen_server.game.repository.InMemoryGameRepository;
import com.WhoisntCitizen_server.game.result.GameResultRecorder;
import com.WhoisntCitizen_server.game.scheduling.LocalGameTimer;
import com.WhoisntCitizen_server.game.scheduling.SchedulerDeferredEventPublisher;
import com.WhoisntCitizen_server.jobs.domain.ActionCode;
import com.WhoisntCitizen_server.jobs.domain.Faction;
import com.WhoisntCitizen_server.jobs.domain.RoleDefinition;
import com.WhoisntCitizen_server.night.service.NightActionResolver;
import com.WhoisntCitizen_server.support.ManualTaskScheduler;
import com.WhoisntCitizen_server.support.MutableClock;
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
 * [서버 여러 대] 연결 끊김 검사의 "잠금 전 미리 확인" (멀티서버 4단계, GameFlowService.checkInactivePlayers)
 *
 * 검사는 5초마다·게임마다·서버마다 불린다. 끊긴 사람이 없으면 게임 잠금을 잡지 않고,
 * 있을 때만 잠금을 잡은 뒤 잠금 안에서 다시 읽어 판단하는지 확인한다.
 * 잠금을 잡은 횟수는 진짜 잠금(LocalGameLock)을 감싼 CountingGameLock으로 센다.
 */
class GameFlowServiceInactivePrecheckTest {

    private static final RoleDefinition RAIDER =
            new RoleDefinition("PIRATE_RAIDER", "해적", Faction.PIRATE, ActionCode.SELECT_ATTACK_TARGET);
    private static final RoleDefinition CAPTAIN =
            new RoleDefinition("CREW_CAPTAIN", "선장", Faction.CREW, ActionCode.INVESTIGATE_FACTION);
    private static final RoleDefinition SAILOR =
            new RoleDefinition("CREW_SAILOR", "선원", Faction.CREW, null);

    private static final Instant NOW = Instant.parse("2026-10-19T12:00:00Z");
    // 밤 30, 결과 5, 낮 60, 투표 30, 처형 5, 종료 후 60초 보관, 10초 동안 요청이 없으면 이탈, 5초마다 검사, 10일 무사망 시 취소, 낮 연장 10
    private static final GamePhaseProperties PROPS = new GamePhaseProperties(30, 5, 60, 30, 5, 60, 10, 5, 10, 10);

    /** 진짜 잠금을 감싸서, 잠금을 잡은 횟수를 세고 잠금 직전에 할 일(beforeLock)을 끼워 넣을 수 있게 한다 */
    private static final class CountingGameLock implements GameLock {
        private final GameLock delegate = new LocalGameLock();
        final AtomicInteger count = new AtomicInteger();
        Runnable beforeLock = () -> { };

        @Override
        public <T> T withLock(String gameId, Supplier<T> action) {
            count.incrementAndGet();
            beforeLock.run();
            return delegate.withLock(gameId, action);
        }
    }

    private MutableClock clock;
    private ManualTaskScheduler scheduler;
    private InMemoryGameRepository repository;
    private LocalPlayerActivityTracker tracker;
    private CountingGameLock lock;
    private GameFlowService flow;
    // 1: 해적, 2: 선장, 3~5: 선원
    private Game game;

    @BeforeEach
    void setUp() {
        clock = new MutableClock(NOW);
        scheduler = new ManualTaskScheduler(clock);
        repository = new InMemoryGameRepository();
        tracker = new LocalPlayerActivityTracker();
        lock = new CountingGameLock();
        GameFlowService[] holder = new GameFlowService[1];
        holder[0] = new GameFlowService(repository, new NightActionResolver(new Random(0)), new VoteResolver(),
                new WinConditionChecker(), new LocalGameTimer(scheduler, () -> holder[0]),
                new SchedulerDeferredEventPublisher(scheduler, clock, event -> { }),
                lock, tracker, PROPS, clock, event -> { }, GameResultRecorder.NONE);
        flow = holder[0];

        game = new Game("1", List.of(
                new GamePlayer(1L, "해적", RAIDER),
                new GamePlayer(2L, "선장", CAPTAIN),
                new GamePlayer(3L, "선원3", SAILOR),
                new GamePlayer(4L, "선원4", SAILOR),
                new GamePlayer(5L, "선원5", SAILOR)), true);
        repository.save(game);
        flow.begin(game.getGameId());
    }

    /** 10초 기준을 넘기도록 11초를 보내면서, keep에 있는 사람만 계속 요청을 보낸 것으로 기록한다 */
    private void passInactiveTimeout(Long... keep) {
        scheduler.advance(Duration.ofSeconds(11));
        for (Long id : keep) {
            tracker.touch(game.getGameId(), id, clock.instant());
        }
    }

    @Test
    void 끊긴_사람이_없으면_게임_잠금을_잡지_않는다() {
        passInactiveTimeout(1L, 2L, 3L, 4L, 5L);
        lock.count.set(0); // 시작·타이머가 잡은 잠금은 세지 않는다

        flow.checkInactivePlayers(game.getGameId());

        assertThat(lock.count.get()).isZero();
        assertThat(game.getPlayer(5L).isDeparted()).isFalse();
    }

    @Test
    void 끊긴_사람이_있으면_잠금을_잡고_처리한다() {
        passInactiveTimeout(1L, 2L, 3L, 4L); // 5번만 요청이 끊김
        lock.count.set(0);

        flow.checkInactivePlayers(game.getGameId());

        assertThat(lock.count.get()).isEqualTo(1);
        assertThat(game.getPlayer(5L).isDeparted()).isTrue();
    }

    @Test
    void 미리_확인과_잠금_사이에_다시_요청이_오면_잠금_안에서_다시_보고_처리하지_않는다() {
        passInactiveTimeout(1L, 2L, 3L, 4L); // 미리 확인 때는 5번이 끊긴 것으로 보인다
        // 잠금을 기다리는 사이 5번의 요청이 도착한 상황
        lock.beforeLock = () -> tracker.touch(game.getGameId(), 5L, clock.instant());
        lock.count.set(0);

        flow.checkInactivePlayers(game.getGameId());

        assertThat(lock.count.get()).isEqualTo(1); // 미리 확인은 "있음"이라 잠금은 잡았지만
        assertThat(game.getPlayer(5L).isDeparted()).isFalse(); // 잠금 안에서 최신 기록으로 다시 보고 내보내지 않는다
    }

    @Test
    void 검사가_꺼져_있으면_미리_확인도_잠금도_하지_않는다() {
        // 기준 0 = 꺼짐 (local 프로필)
        GamePhaseProperties off = new GamePhaseProperties(30, 5, 60, 30, 5, 60, 0, 5, 10, 10);
        CountingGameLock offLock = new CountingGameLock();
        GameFlowService[] holder = new GameFlowService[1];
        holder[0] = new GameFlowService(repository, new NightActionResolver(new Random(0)), new VoteResolver(),
                new WinConditionChecker(), new LocalGameTimer(scheduler, () -> holder[0]),
                new SchedulerDeferredEventPublisher(scheduler, clock, event -> { }),
                offLock, tracker, off, clock, event -> { }, GameResultRecorder.NONE);
        passInactiveTimeout(1L, 2L, 3L, 4L);

        holder[0].checkInactivePlayers(game.getGameId());

        assertThat(offLock.count.get()).isZero();
        assertThat(game.getPlayer(5L).isDeparted()).isFalse();
    }
}
