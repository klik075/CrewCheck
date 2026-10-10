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
import org.springframework.data.redis.core.ZSetOperations;
import org.springframework.scheduling.concurrent.ThreadPoolTaskScheduler;
import org.testcontainers.DockerClientFactory;
import org.testcontainers.containers.GenericContainer;
import org.testcontainers.utility.DockerImageName;

import java.time.Clock;
import java.time.Duration;
import java.time.Instant;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;
import java.util.Random;
import java.util.Set;
import java.util.concurrent.TimeUnit;
import java.util.function.BooleanSupplier;

import static org.assertj.core.api.Assertions.assertThat;
import static org.junit.jupiter.api.Assumptions.assumeTrue;

/**
 * 3-6: 게임 진행 전체를 Redis 타이머(+ Redis 게임 저장소)로 돌리는 통합 테스트. Docker가 꺼져 있으면 건너뛴다.
 *
 * 3-3·3-4의 테스트는 타이머 혼자를 확인했다. 여기서는 실제 GameFlowService가 타이머를 예약하고, 시간이 되면
 * Redis 타이머가 GameFlowService를 불러 페이즈가 넘어가는 흐름 전체를 확인한다. (수동으로 Postman으로 확인한 것을 자동화)
 *
 * "서버"(Server)는 서버 한 대가 들고 있는 것(타이머, 게임 진행, 잠금)을 모은 것이다. 같은 Redis를 바라보는 Server를
 * 여러 개 만들면 서버 여러 대, 버리고 새로 만들면 재시작이다. 시간은 MutableClock으로 움직이고,
 * 시간이 된 예약은 그 서버의 timer.pollOnce()로 실행한다. (마지막 테스트만 실제 확인 작업·작업 스레드로 돈다)
 *
 * 게임은 5명(해적 1, 선장 1, 선원 3)이고 아무도 능력·투표를 하지 않는다. 그래서 아무도 죽지 않고 시간으로만 넘어간다.
 */
class RedisTimerGameFlowTest {

    private static final Instant NOW = Instant.parse("2026-10-11T12:00:00Z");
    // 밤 30, 결과 5, 낮 60, 투표 30, 처형 5. 종료 후 60초 보관, 연결 끊김 검사 끔, 10일 무사망 시 취소
    private static final GamePhaseProperties PROPS = new GamePhaseProperties(30, 5, 60, 30, 5, 60, 0, 5, 10);
    private static final String TIMERS_KEY = "game:v1:timers";

    private static GenericContainer<?> redisContainer;
    private static GameRedis gameRedis;
    private static GameRedisProperties redisProps;

    private MutableClock clock;
    private String gameId;

    /** 서버 한 대. 같은 Redis를 보지만 타이머·게임 진행·잠금·저장소 객체는 서버마다 따로다. */
    private record Server(RedisGameRepository repository, RedisGameTimer timer, GameFlowService flow) {
    }

    @BeforeAll
    static void startRedis() {
        assumeTrue(DockerClientFactory.instance().isDockerAvailable(),
                "Docker가 실행 중이 아니라 Redis 타이머 게임 흐름 테스트를 건너뜁니다");
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
        gameId = server().repository().save(newGame()).getGameId();
    }

    private static Game newGame() {
        return new Game("1", List.of(
                new GamePlayer(1L, "해적", TestRoles.RAIDER),
                new GamePlayer(2L, "선장", TestRoles.CAPTAIN),
                new GamePlayer(3L, "선원3", TestRoles.SAILOR),
                new GamePlayer(4L, "선원4", TestRoles.SAILOR),
                new GamePlayer(5L, "선원5", TestRoles.SAILOR)), true);
    }

    private Server server() {
        return server(PROPS, clock);
    }

    /** 새 서버를 만든다. 재시작이나 다른 서버를 흉내 낼 때 다시 부른다. */
    private Server server(GamePhaseProperties props, Clock serverClock) {
        RedisGameRepository repository = new RedisGameRepository(gameRedis, new GameSnapshotCodec(), redisProps, serverClock);
        GameFlowService[] flow = new GameFlowService[1];
        RedisGameTimer timer = new RedisGameTimer(gameRedis, () -> flow[0], serverClock);
        ManualTaskScheduler scheduler = new ManualTaskScheduler(new MutableClock(NOW)); // 지연 이벤트(로비·전적)는 여기서 다루지 않음
        flow[0] = new GameFlowService(repository, new NightActionResolver(new Random(0)), new VoteResolver(),
                new WinConditionChecker(), timer, new SchedulerDeferredEventPublisher(scheduler, serverClock, event -> { }),
                new LocalGameLock(), new LocalPlayerActivityTracker(), props, serverClock, event -> { });
        return new Server(repository, timer, flow[0]);
    }

    private Game stored() {
        return server().repository().findById(gameId).orElseThrow();
    }

    /** Redis에 남아 있는 타이머 예약 (member → 시각), 시각 순 */
    private Map<String, Instant> timers() {
        Set<ZSetOperations.TypedTuple<String>> tuples = gameRedis.template().opsForZSet().rangeWithScores(TIMERS_KEY, 0, -1);
        Map<String, Instant> result = new LinkedHashMap<>();
        if (tuples != null) {
            tuples.forEach(t -> result.put(t.getValue(), Instant.ofEpochMilli(t.getScore().longValue())));
        }
        return result;
    }

    private String phaseTimer(long version) {
        return TimerKeys.phase(gameId, version);
    }

    private Instant at(long seconds) {
        return NOW.plusSeconds(seconds);
    }

    /** 시계를 seconds만큼 옮기고 server가 시간이 된 예약을 실행한다. 실행한 수를 돌려준다. */
    private int advance(Server server, long seconds) {
        clock.advance(Duration.ofSeconds(seconds));
        return server.timer().pollOnce();
    }

    private void assertPhase(GamePhase phase, int day, long version) {
        Game game = stored();
        assertThat(game.getPhase()).isEqualTo(phase);
        assertThat(game.getDay()).isEqualTo(day);
        assertThat(game.getPhaseVersion()).isEqualTo(version);
    }

    // ---------- 1. 시간으로만 한 바퀴 ----------

    @Test
    void 시간이_지나면_Redis_타이머로_밤부터_다음_밤까지_한_바퀴_돈다() {
        Server server = server();
        server.flow().begin(gameId);

        assertPhase(GamePhase.NIGHT, 1, 1);
        assertThat(timers()).containsExactly(Map.entry(phaseTimer(1), at(30)));

        advance(server, 30);                                       // 밤 30초
        assertPhase(GamePhase.NIGHT_RESULT, 1, 2);
        assertThat(timers()).as("실행한 예약은 지워지고 다음 페이즈 예약만 남는다")
                .containsExactly(Map.entry(phaseTimer(2), at(35)));

        advance(server, 5);                                        // 밤 결과 5초
        assertPhase(GamePhase.DAY, 1, 3);
        advance(server, 60);                                       // 낮 60초
        assertPhase(GamePhase.VOTE, 1, 4);
        advance(server, 30);                                       // 투표 30초 (아무도 투표 안 함 → 처형 없음)
        assertPhase(GamePhase.EXECUTION, 1, 5);
        advance(server, 5);                                        // 처형 5초
        assertPhase(GamePhase.NIGHT, 2, 6);

        assertThat(timers()).containsExactly(Map.entry(phaseTimer(6), at(30 + 5 + 60 + 30 + 5 + 30)));
    }

    @Test
    void 시간이_되기_전에는_넘어가지_않는다() {
        Server server = server();
        server.flow().begin(gameId);

        assertThat(advance(server, 29)).isZero();

        assertPhase(GamePhase.NIGHT, 1, 1);
    }

    // ---------- 2. 다른 경로로 먼저 넘어간 경우 ----------

    @Test
    void 전원_제출처럼_먼저_넘어가면_남은_옛_타이머는_버전이_달라_무시되고_지워진다() {
        Server server = server();
        server.flow().begin(gameId);
        server.flow().onPhaseTimeout(gameId, 1);                   // 시간 전에 밤이 끝남 (전원 제출과 같은 효과)

        assertPhase(GamePhase.NIGHT_RESULT, 1, 2);
        assertThat(timers()).as("옛 밤 타이머(:1)는 아직 남아 있다")
                .containsOnlyKeys(phaseTimer(1), phaseTimer(2));

        advance(server, 5);                                        // 밤 결과 끝 → 낮
        assertPhase(GamePhase.DAY, 1, 3);

        assertThat(advance(server, 25)).as("원래 밤 종료 시각(30초)에 옛 타이머가 실행된다").isEqualTo(1);
        assertPhase(GamePhase.DAY, 1, 3);                          // 버전이 달라 아무것도 바뀌지 않음
        assertThat(timers()).containsOnlyKeys(phaseTimer(3));      // 옛 타이머는 지워짐
    }

    // ---------- 3. 서버 재시작 ----------

    @Test
    void 서버를_다시_켜도_Redis에_남은_예약으로_이어서_진행된다() {
        server().flow().begin(gameId);                             // 이 서버는 이후 꺼졌다고 본다

        Server restarted = server();                               // 새 타이머·게임 진행·잠금 (복구 작업 없음)
        advance(restarted, 30);

        assertPhase(GamePhase.NIGHT_RESULT, 1, 2);
    }

    @Test
    void 꺼져_있는_동안_시간이_지났으면_켜자마자_넘어간다() {
        server().flow().begin(gameId);
        clock.advance(Duration.ofSeconds(120));                    // 꺼져 있는 동안 밤 종료 시각(30초)이 지남

        Server restarted = server();
        assertThat(restarted.timer().pollOnce()).isEqualTo(1);     // 켜고 처음 확인할 때

        assertPhase(GamePhase.NIGHT_RESULT, 1, 2);
    }

    // ---------- 4. 서버 두 대 ----------

    @Test
    void 서버_두_대가_번갈아_확인해도_페이즈는_한_번씩만_넘어간다() {
        Server a = server();
        Server b = server();
        a.flow().begin(gameId);

        clock.advance(Duration.ofSeconds(30));
        assertThat(b.timer().pollOnce()).isEqualTo(1);             // B가 먼저 확인해 가져감
        assertThat(a.timer().pollOnce()).isZero();                 // A에는 남은 것이 없음
        assertPhase(GamePhase.NIGHT_RESULT, 1, 2);

        clock.advance(Duration.ofSeconds(5));
        assertThat(a.timer().pollOnce()).isEqualTo(1);             // 이번엔 A가 먼저
        assertThat(b.timer().pollOnce()).isZero();
        assertPhase(GamePhase.DAY, 1, 3);
    }

    @Test
    void 가져간_서버가_실행하지_못하고_죽어도_lease가_지나면_다른_서버가_이어서_진행한다() {
        Server dying = server();
        dying.flow().begin(gameId);
        clock.advance(Duration.ofSeconds(30));
        assertThat(dying.timer().claimDue()).hasSize(1);           // 가져가기만 하고 죽음

        Server survivor = server();
        clock.advance(RedisGameTimer.DEFAULT_LEASE.minusSeconds(1));
        assertThat(survivor.timer().pollOnce()).as("lease 동안은 기다린다").isZero();
        assertPhase(GamePhase.NIGHT, 1, 1);

        clock.advance(Duration.ofSeconds(1));
        assertThat(survivor.timer().pollOnce()).isEqualTo(1);

        assertPhase(GamePhase.NIGHT_RESULT, 1, 2);
    }

    // ---------- 5. 게임 종료와 정리 ----------

    @Test
    void 게임이_끝나면_정리_타이머가_예약되고_시간이_지나면_상태와_타이머가_모두_지워진다() {
        // 하루 동안 아무도 죽지 않으면 취소되게 해서 첫 투표가 끝날 때 게임을 끝낸다
        GamePhaseProperties endAfterOneDay = new GamePhaseProperties(30, 5, 60, 30, 5, 60, 0, 5, 1);
        Server server = server(endAfterOneDay, clock);
        server.flow().begin(gameId);
        advance(server, 30);
        advance(server, 5);
        advance(server, 60);
        advance(server, 30);                                       // 첫 투표 종료 → 사망자 없음 → 게임 취소

        assertThat(stored().isEnded()).isTrue();
        assertThat(timers()).containsExactly(Map.entry(TimerKeys.cleanup(gameId), at(125 + 60)));
        assertThat(server.repository().findActiveIds()).isEmpty();

        assertThat(advance(server, 59)).isZero();                  // 결과 조회 시간(60초) 동안은 남아 있음
        assertThat(server.repository().findById(gameId)).isPresent();

        advance(server, 1);

        assertThat(server.repository().findById(gameId)).isEmpty();
        assertThat(timers()).isEmpty();
    }

    // ---------- 6. 실제 확인 작업과 작업 스레드 ----------

    @Test
    void 실제_확인_작업과_작업_스레드로도_페이즈가_넘어간다() throws Exception {
        // 서버에서처럼 RedisTimerPoller(주기 확인) → RedisTimerDispatcher(작업 스레드)로 돈다. 시계는 실제 시계, 페이즈는 1초씩
        GamePhaseProperties oneSecondPhases = new GamePhaseProperties(1, 1, 1, 1, 1, 60, 0, 5, 10);
        Clock realClock = Clock.systemUTC();
        Server server = server(oneSecondPhases, realClock);
        ThreadPoolTaskScheduler scheduler = new ThreadPoolTaskScheduler();
        scheduler.setPoolSize(2);
        scheduler.initialize();
        RedisTimerDispatcher dispatcher = new RedisTimerDispatcher(server.timer(), 2);
        RedisTimerPoller poller = new RedisTimerPoller(dispatcher::dispatchOnce, 2, scheduler, realClock, Duration.ofMillis(50));
        try {
            poller.start();
            server.flow().begin(gameId);

            // 밤 → 밤 결과 → 낮 → 투표 → 처형 → 2일차 밤 (각 1초)
            assertThat(eventually(Duration.ofSeconds(15), () -> {
                Game game = stored();
                return game.getDay() == 2 && game.getPhase() == GamePhase.NIGHT;
            })).as("15초 안에 2일차 밤까지 넘어간다").isTrue();
            assertThat(stored().getPhaseVersion()).isEqualTo(6);
        } finally {
            poller.stop();
            dispatcher.close();
            scheduler.shutdown();
        }
    }

    private static boolean eventually(Duration timeout, BooleanSupplier condition) throws InterruptedException {
        long deadline = System.nanoTime() + timeout.toNanos();
        while (System.nanoTime() < deadline) {
            if (condition.getAsBoolean()) {
                return true;
            }
            TimeUnit.MILLISECONDS.sleep(50);
        }
        return condition.getAsBoolean();
    }
}
