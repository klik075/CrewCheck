package com.WhoisntCitizen_server.game.scheduling.redis;

import com.WhoisntCitizen_server.common.config.GamePhaseProperties;
import com.WhoisntCitizen_server.game.activity.LocalPlayerActivityTracker;
import com.WhoisntCitizen_server.game.entity.Game;
import com.WhoisntCitizen_server.game.entity.GamePhase;
import com.WhoisntCitizen_server.game.entity.GamePlayer;
import com.WhoisntCitizen_server.game.lock.LocalGameLock;
import com.WhoisntCitizen_server.game.repository.redis.GameRedis;
import com.WhoisntCitizen_server.game.repository.redis.GameRedisProperties;
import com.WhoisntCitizen_server.game.repository.redis.RedisGameRepository;
import com.WhoisntCitizen_server.game.repository.snapshot.GameSnapshotCodec;
import com.WhoisntCitizen_server.game.scheduling.SchedulerDeferredEventPublisher;
import com.WhoisntCitizen_server.game.scheduling.TimerKeys;
import com.WhoisntCitizen_server.game.service.GameFlowService;
import com.WhoisntCitizen_server.game.service.WinConditionChecker;
import com.WhoisntCitizen_server.night.service.NightActionResolver;
import com.WhoisntCitizen_server.support.ManualTaskScheduler;
import com.WhoisntCitizen_server.support.MutableClock;
import com.WhoisntCitizen_server.support.TestRoles;
import com.WhoisntCitizen_server.vote.service.VoteResolver;
import org.junit.jupiter.api.AfterAll;
import org.junit.jupiter.api.BeforeAll;
import org.junit.jupiter.api.BeforeEach;
import org.junit.jupiter.api.Test;
import org.testcontainers.DockerClientFactory;
import org.testcontainers.containers.GenericContainer;
import org.testcontainers.utility.DockerImageName;

import java.time.Duration;
import java.time.Instant;
import java.util.List;
import java.util.Random;

import static org.assertj.core.api.Assertions.assertThat;
import static org.junit.jupiter.api.Assumptions.assumeTrue;

/**
 * 3-7: 멈춘 게임 감시(StuckGameWatchdog)를 실제 Redis로 확인한다. Docker가 꺼져 있으면 건너뛴다.
 *
 * "저장 직후·예약 전에 서버가 죽은" 상황은 게임을 시작한 뒤 Redis에서 그 페이즈의 타이머 예약을 지워서 만든다.
 * (상태는 "밤이 30초에 끝난다"인데 밤을 끝낼 예약이 없는 상태)
 */
class StuckGameWatchdogTest {

    private static final Instant NOW = Instant.parse("2026-10-11T12:00:00Z");
    // 밤 30, 결과 5, 낮 60, 투표 30, 처형 5. 종료 후 60초 보관, 연결 끊김 검사 끔, 10일 무사망 시 취소
    private static final GamePhaseProperties PROPS = new GamePhaseProperties(30, 5, 60, 30, 5, 60, 0, 5, 10);
    private static final String TIMERS_KEY = "game:v1:timers";

    private static GenericContainer<?> redisContainer;
    private static GameRedis gameRedis;
    private static GameRedisProperties redisProps;

    private MutableClock clock;
    private RedisGameRepository repository;
    private RedisGameTimer timer;
    private GameFlowService flow;
    private StuckGameWatchdog watchdog;
    private String gameId;

    @BeforeAll
    static void startRedis() {
        assumeTrue(DockerClientFactory.instance().isDockerAvailable(),
                "Docker가 실행 중이 아니라 멈춘 게임 감시 테스트를 건너뜁니다");
        redisContainer = new GenericContainer<>(DockerImageName.parse("redis:7-alpine")).withExposedPorts(6379);
        redisContainer.start();
        redisProps = new GameRedisProperties(redisContainer.getHost(), redisContainer.getMappedPort(6379),
                0, "", 21600, 600);
        gameRedis = new GameRedis(redisProps);
    }

    @AfterAll
    static void stopRedis() {
        if (gameRedis != null) {
            gameRedis.destroy();
        }
        if (redisContainer != null) {
            redisContainer.stop();
        }
    }

    @BeforeEach
    void setUp() {
        gameRedis.template().getConnectionFactory().getConnection().serverCommands().flushDb();
        clock = new MutableClock(NOW);
        repository = new RedisGameRepository(gameRedis, new GameSnapshotCodec(), redisProps, clock);
        GameFlowService[] holder = new GameFlowService[1];
        timer = new RedisGameTimer(gameRedis, () -> holder[0], clock);
        holder[0] = new GameFlowService(repository, new NightActionResolver(new Random(0)), new VoteResolver(),
                new WinConditionChecker(), timer,
                new SchedulerDeferredEventPublisher(new ManualTaskScheduler(new MutableClock(NOW)), clock, event -> { }),
                new LocalGameLock(), new LocalPlayerActivityTracker(), PROPS, clock, event -> { });
        flow = holder[0];
        watchdog = new StuckGameWatchdog(repository, timer, new ManualTaskScheduler(clock), clock);
        gameId = repository.save(newGame()).getGameId();
    }

    private static Game newGame() {
        return new Game("1", List.of(
                new GamePlayer(1L, "해적", TestRoles.RAIDER),
                new GamePlayer(2L, "선장", TestRoles.CAPTAIN),
                new GamePlayer(3L, "선원3", TestRoles.SAILOR),
                new GamePlayer(4L, "선원4", TestRoles.SAILOR),
                new GamePlayer(5L, "선원5", TestRoles.SAILOR)), true);
    }

    private Game stored() {
        return repository.findById(gameId).orElseThrow();
    }

    /** 게임을 시작하고, 첫 밤 타이머 예약을 지워 "저장은 됐는데 예약 전에 죽은" 상태를 만든다. */
    private void startGameAndLoseTimer() {
        flow.begin(gameId);
        gameRedis.template().opsForZSet().remove(TIMERS_KEY, TimerKeys.phase(gameId, 1));
    }

    private Double score(String member) {
        return gameRedis.template().opsForZSet().score(TIMERS_KEY, member);
    }

    // ---------- 멈춘 게임 ----------

    @Test
    void 예약이_빠져_멈춘_게임을_찾아_타이머를_다시_걸고_게임이_이어진다() {
        startGameAndLoseTimer();
        clock.advance(Duration.ofSeconds(30).plus(StuckGameWatchdog.GRACE).plusSeconds(1));
        assertThat(timer.pollOnce()).as("예약이 없어 아무것도 실행되지 않는다 (멈춤)").isZero();

        assertThat(watchdog.checkOnce()).isEqualTo(1);
        assertThat(score(TimerKeys.phase(gameId, 1))).as("지금으로 다시 걸림").isEqualTo((double) clock.millis());

        timer.pollOnce();
        assertThat(stored().getPhase()).isEqualTo(GamePhase.NIGHT_RESULT);
        assertThat(stored().getPhaseVersion()).isEqualTo(2);
    }

    @Test
    void 끝날_시각에서_여유_시간이_지나기_전이면_건드리지_않는다() {
        startGameAndLoseTimer();
        clock.advance(Duration.ofSeconds(30).plus(StuckGameWatchdog.GRACE).minusSeconds(1));

        assertThat(watchdog.checkOnce()).isZero();

        assertThat(score(TimerKeys.phase(gameId, 1))).isNull();
    }

    // ---------- 멈추지 않은 게임 ----------

    @Test
    void 예약이_있으면_늦어지고_있어도_건드리지_않는다() {
        flow.begin(gameId);
        clock.advance(Duration.ofSeconds(60));                   // 시간은 지났지만 아직 실행되지 않음 (작업 스레드가 바쁜 등)

        assertThat(watchdog.checkOnce()).isZero();

        assertThat(score(TimerKeys.phase(gameId, 1))).as("원래 시각 그대로")
                .isEqualTo((double) NOW.plusSeconds(30).toEpochMilli());
    }

    @Test
    void 다른_서버가_가져가_실행_중인_예약은_되돌리지_않는다() {
        flow.begin(gameId);
        clock.advance(Duration.ofSeconds(30));
        long leaseUntil = timer.claimDue().get(0).leaseUntil();  // 다른 서버가 가져감 (점수 = 지금 + lease)
        clock.advance(StuckGameWatchdog.GRACE.plusSeconds(1));

        assertThat(watchdog.checkOnce()).isZero();

        assertThat(score(TimerKeys.phase(gameId, 1))).as("가져간 서버의 점수 그대로").isEqualTo((double) leaseUntil);
    }

    @Test
    void 첫_밤_시작_전에_멈춘_게임은_경고만_남기고_건너뛴다() {
        // 시작 전 게임 (phase 없음): 걸 타이머가 없다
        clock.advance(Duration.ofMinutes(10));
        assertThat(watchdog.checkOnce()).isZero();
        assertThat(gameRedis.template().opsForZSet().zCard(TIMERS_KEY)).isZero();
    }

    @Test
    void 여러_서버가_동시에_감시해도_예약은_하나만_생긴다() {
        startGameAndLoseTimer();
        clock.advance(Duration.ofSeconds(60));
        StuckGameWatchdog otherServer = new StuckGameWatchdog(repository, timer, new ManualTaskScheduler(clock), clock);

        assertThat(watchdog.checkOnce()).isEqualTo(1);
        assertThat(otherServer.checkOnce()).as("이미 다시 걸려 있어 건드리지 않는다").isZero();
        assertThat(gameRedis.template().opsForZSet().zCard(TIMERS_KEY)).isEqualTo(1);
    }

    // ---------- 켜고 끄기 ----------

    @Test
    void 켜면_감시_주기마다_확인한다() {
        ManualTaskScheduler scheduler = new ManualTaskScheduler(clock);
        StuckGameWatchdog periodic = new StuckGameWatchdog(repository, timer, scheduler, clock);
        startGameAndLoseTimer();

        periodic.start();
        scheduler.advance(StuckGameWatchdog.CHECK_INTERVAL);     // 30초: 아직 끝날 시각 + 여유(40초) 전
        assertThat(score(TimerKeys.phase(gameId, 1))).isNull();
        scheduler.advance(StuckGameWatchdog.CHECK_INTERVAL);     // 60초: 멈춘 것으로 보고 다시 건다

        assertThat(score(TimerKeys.phase(gameId, 1))).isNotNull();

        periodic.stop();
        assertThat(periodic.isRunning()).isFalse();
    }
}
