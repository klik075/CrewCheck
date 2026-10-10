package com.WhoisntCitizen_server.game.activity.redis;

import com.WhoisntCitizen_server.common.config.GamePhaseProperties;
import com.WhoisntCitizen_server.game.activity.LocalPlayerActivityTracker;
import com.WhoisntCitizen_server.game.activity.PlayerActivityTracker;
import com.WhoisntCitizen_server.game.entity.Game;
import com.WhoisntCitizen_server.game.entity.GameEndReason;
import com.WhoisntCitizen_server.game.entity.GamePhase;
import com.WhoisntCitizen_server.game.entity.GamePlayer;
import com.WhoisntCitizen_server.game.lock.LocalGameLock;
import com.WhoisntCitizen_server.game.repository.redis.GameRedis;
import com.WhoisntCitizen_server.game.repository.redis.GameRedisProperties;
import com.WhoisntCitizen_server.game.repository.redis.RedisGameRepository;
import com.WhoisntCitizen_server.game.repository.snapshot.GameSnapshotCodec;
import com.WhoisntCitizen_server.game.scheduling.SchedulerDeferredEventPublisher;
import com.WhoisntCitizen_server.game.scheduling.redis.RedisGameTimer;
import com.WhoisntCitizen_server.game.service.GameFlowService;
import com.WhoisntCitizen_server.game.service.GameService;
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

import java.time.Clock;
import java.time.Duration;
import java.time.Instant;
import java.util.List;
import java.util.Random;
import java.util.function.Supplier;

import static org.assertj.core.api.Assertions.assertThat;
import static org.junit.jupiter.api.Assumptions.assumeTrue;

/**
 * 3.5-6: 서버 A가 받은 요청(접속 기록)을 서버 B의 미접속 검사가 보는지 확인하는 통합 테스트. Docker가 꺼져 있으면 건너뛴다.
 *
 * "서버"(Server)는 서버 한 대가 들고 있는 것(게임 서비스, 게임 진행, 접속 기록, 잠금, 타이머)을 모은 것이다.
 * 같은 Redis를 바라보는 Server를 두 개 만들면 서버 두 대다. (RedisTimerGameFlowTest와 같은 방식)
 *  - 요청: server.games().getState(gameId, playerId) — 실제 상태 조회 API가 부르는 메서드라 참가자 확인 + touch까지 지난다
 *  - 미접속 검사: server.flow().checkInactivePlayers(gameId) — InactivePlayerMonitor가 주기적으로 부르는 메서드
 *
 * 연결 끊김 기준은 60초다. 시간은 MutableClock으로 움직인다. 페이즈 타이머는 일부러 실행하지 않아 계속 첫 밤이다.
 * 게임은 5명(해적 1, 선장 1, 선원 3)이다.
 */
class RedisActivityMultiServerTest {

    private static final Instant NOW = Instant.parse("2026-10-11T12:00:00Z");
    // 밤 30, 결과 5, 낮 60, 투표 30, 처형 5. 종료 후 60초 보관, 60초 동안 요청이 없으면 연결 끊김, 5초마다 검사, 10일 무사망 시 취소
    private static final GamePhaseProperties PROPS = new GamePhaseProperties(30, 5, 60, 30, 5, 60, 60, 5, 10);
    private static final List<Long> EVERYONE = List.of(1L, 2L, 3L, 4L, 5L);

    private static GenericContainer<?> redisContainer;
    private static GameRedis gameRedis;
    private static GameRedisProperties redisProps;

    private MutableClock clock;
    private String gameId;

    /** 서버 한 대. 같은 Redis를 보지만 객체(접속 기록 포함)는 서버마다 따로 만든다. */
    private record Server(RedisGameRepository repository, RedisGameTimer timer, GameFlowService flow,
                          GameService games, PlayerActivityTracker activity) {

        /** 플레이어들이 이 서버로 상태 조회 요청을 보낸다 */
        void request(String gameId, List<Long> playerIds) {
            playerIds.forEach(playerId -> games.getState(gameId, playerId));
        }
    }

    @BeforeAll
    static void startRedis() {
        assumeTrue(DockerClientFactory.instance().isDockerAvailable(),
                "Docker가 실행 중이 아니라 서버 여러 대 접속 기록 테스트를 건너뜁니다");
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
        gameId = redisServer(clock).repository().save(newGame()).getGameId();
    }

    private static Game newGame() {
        return new Game("1", List.of(
                new GamePlayer(1L, "해적", TestRoles.RAIDER),
                new GamePlayer(2L, "선장", TestRoles.CAPTAIN),
                new GamePlayer(3L, "선원3", TestRoles.SAILOR),
                new GamePlayer(4L, "선원4", TestRoles.SAILOR),
                new GamePlayer(5L, "선원5", TestRoles.SAILOR)), true);
    }

    /** Redis 접속 기록을 쓰는 서버 (mafia.game.activity=redis) */
    private Server redisServer(Clock serverClock) {
        return server(serverClock, () -> new RedisPlayerActivityTracker(gameRedis));
    }

    /** 서버 메모리 접속 기록을 쓰는 서버 (mafia.game.activity=local). 대조용 */
    private Server localServer(Clock serverClock) {
        return server(serverClock, LocalPlayerActivityTracker::new);
    }

    private Server server(Clock serverClock, Supplier<PlayerActivityTracker> activityFactory) {
        PlayerActivityTracker activity = activityFactory.get();
        RedisGameRepository repository = new RedisGameRepository(gameRedis, new GameSnapshotCodec(), redisProps, serverClock);
        GameFlowService[] flow = new GameFlowService[1];
        RedisGameTimer timer = new RedisGameTimer(gameRedis, () -> flow[0], serverClock);
        LocalGameLock lock = new LocalGameLock();
        ManualTaskScheduler scheduler = new ManualTaskScheduler(new MutableClock(NOW)); // 지연 이벤트(로비·전적)는 여기서 다루지 않음
        flow[0] = new GameFlowService(repository, new NightActionResolver(new Random(0)), new VoteResolver(),
                new WinConditionChecker(), timer, new SchedulerDeferredEventPublisher(scheduler, serverClock, event -> { }),
                lock, activity, PROPS, serverClock, event -> { });
        GameService games = new GameService(repository, TestRoles.assigner(7), flow[0], lock, activity, serverClock);
        return new Server(repository, timer, flow[0], games, activity);
    }

    private Game stored() {
        return redisServer(clock).repository().findById(gameId).orElseThrow();
    }

    private void advanceTo(long seconds) {
        clock.set(NOW.plusSeconds(seconds));
    }

    private Instant at(long seconds) {
        return NOW.plusSeconds(seconds);
    }

    // ---------- 1. 다른 서버가 받은 요청을 본다 ----------

    @Test
    void 서버_A가_받은_요청을_서버_B의_미접속_검사가_본다() {
        Server serverA = redisServer(clock);
        Server serverB = redisServer(clock);
        serverB.flow().begin(gameId);                 // 게임 시작은 서버 B (시작 시각 0초로 전원 기록)

        advanceTo(50);
        serverA.request(gameId, EVERYONE);            // 그 뒤 요청은 모두 서버 A로 간다 (로드 밸런서)

        advanceTo(70);                                // 시작(0초)으로부터는 60초가 넘었지만, 마지막 요청(50초)으로부터는 20초
        serverB.flow().checkInactivePlayers(gameId);

        Game game = stored();
        assertThat(game.isEnded()).isFalse();
        assertThat(game.getPhase()).isEqualTo(GamePhase.NIGHT);
        assertThat(game.getPlayers()).allSatisfy(p -> {
            assertThat(p.isAlive()).isTrue();
            assertThat(p.isDeparted()).isFalse();
        });
        assertThat(serverB.activity().lastSeen(gameId)).as("서버 B가 서버 A의 기록을 그대로 본다")
                .containsOnlyKeys(EVERYONE).allSatisfy((playerId, seen) -> assertThat(seen).isEqualTo(at(50)));
    }

    @Test
    void 요청이_끊긴_플레이어만_서버_B가_내보낸다() {
        Server serverA = redisServer(clock);
        Server serverB = redisServer(clock);
        serverA.flow().begin(gameId);

        advanceTo(50);
        serverA.request(gameId, List.of(1L, 2L, 4L, 5L));   // 선원3(3번)만 시작 뒤로 요청이 없다

        advanceTo(70);
        serverB.flow().checkInactivePlayers(gameId);

        Game game = stored();
        assertThat(game.getPlayer(3L).isDeparted()).isTrue();
        assertThat(game.getPlayer(3L).isAlive()).isFalse();
        assertThat(List.of(1L, 2L, 4L, 5L)).allSatisfy(id -> assertThat(game.getPlayer(id).isAlive()).isTrue());
        assertThat(game.isEnded()).as("해적 1 : 시민 3이라 게임은 계속된다").isFalse();
    }

    @Test
    void 요청이_두_서버로_나뉘어도_가장_늦은_요청을_기준으로_판단한다() {
        // 서버 C는 시계가 30초 늦다. (또는 오래 걸린 요청이 늦게 기록되는 경우)
        MutableClock slowClock = new MutableClock(NOW);
        Server serverA = redisServer(clock);
        Server serverB = redisServer(clock);
        Server serverC = redisServer(slowClock);
        serverA.flow().begin(gameId);

        advanceTo(50);
        serverA.request(gameId, List.of(1L, 2L));
        serverB.request(gameId, List.of(3L, 4L, 5L));
        slowClock.set(at(20));
        serverC.request(gameId, List.of(3L));                // 3번의 기록이 50초 → 20초로 되돌아가면 안 된다

        advanceTo(85);                                       // 기준: 25초. 20초였다면 3번이 끊긴 것으로 판정된다
        serverA.flow().checkInactivePlayers(gameId);

        Game game = stored();
        assertThat(game.getPlayers()).allSatisfy(p -> assertThat(p.isDeparted()).isFalse());
        assertThat(serverA.activity().lastSeen(gameId)).containsEntry(3L, at(50));
    }

    // ---------- 2. 대조: 서버마다 메모리에 기록하면 ----------

    @Test
    void 대조_서버마다_메모리에_기록하면_서버_B가_멀쩡한_플레이어를_모두_내보낸다() {
        Server serverA = localServer(clock);
        Server serverB = localServer(clock);
        serverB.flow().begin(gameId);                 // 서버 B 메모리에만 시작 시각(0초)이 남는다

        advanceTo(50);
        serverA.request(gameId, EVERYONE);            // 서버 A 메모리에만 50초가 남는다

        advanceTo(70);
        serverB.flow().checkInactivePlayers(gameId);  // 서버 B는 0초 기록만 보고 전원이 끊겼다고 판단한다

        Game game = stored();
        assertThat(game.isEnded()).isTrue();
        assertThat(game.getEndReason()).isEqualTo(GameEndReason.CANCELLED_ALL_DISCONNECTED);
    }

    // ---------- 3. 정리 ----------

    @Test
    void 끝난_게임을_서버_B가_정리하면_서버_A가_남긴_기록도_지워진다() {
        Server serverA = redisServer(clock);
        Server serverB = redisServer(clock);
        serverA.flow().begin(gameId);
        advanceTo(10);
        serverA.request(gameId, EVERYONE);

        advanceTo(80);                                // 전원 70초 동안 요청 없음 → 게임 취소
        serverB.flow().checkInactivePlayers(gameId);
        assertThat(stored().getEndReason()).isEqualTo(GameEndReason.CANCELLED_ALL_DISCONNECTED);
        assertThat(gameRedis.template().hasKey("game:v1:activity:" + gameId)).isTrue();

        clock.advance(Duration.ofSeconds(PROPS.endedRetentionSeconds()));
        serverB.timer().pollOnce();                   // 서버 B가 정리 예약을 실행한다

        assertThat(serverB.repository().findById(gameId)).isEmpty();
        assertThat(gameRedis.template().hasKey("game:v1:activity:" + gameId)).isFalse();
        assertThat(serverA.activity().lastSeen(gameId)).isEmpty();
    }
}
