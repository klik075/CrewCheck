package com.WhoisntCitizen_server.game.service;

import com.WhoisntCitizen_server.common.config.GamePhaseProperties;
import com.WhoisntCitizen_server.game.activity.LocalPlayerActivityTracker;
import com.WhoisntCitizen_server.game.entity.Game;
import com.WhoisntCitizen_server.game.entity.GamePhase;
import com.WhoisntCitizen_server.game.entity.GamePlayer;
import com.WhoisntCitizen_server.game.lock.LocalGameLock;
import com.WhoisntCitizen_server.game.scheduling.LocalGameTimer;
import com.WhoisntCitizen_server.night.service.NightActionResolver;
import com.WhoisntCitizen_server.support.CopyingGameRepository;
import com.WhoisntCitizen_server.support.ManualTaskScheduler;
import com.WhoisntCitizen_server.support.MutableClock;
import com.WhoisntCitizen_server.support.TestGameFlows;
import com.WhoisntCitizen_server.support.TestRoles;
import com.WhoisntCitizen_server.vote.service.VoteResolver;
import org.junit.jupiter.api.BeforeEach;
import org.junit.jupiter.api.Test;

import java.time.Duration;
import java.time.Instant;
import java.util.List;
import java.util.Random;

import static org.assertj.core.api.Assertions.assertThat;

/**
 * 1-8: 서버 재시작 후 진행 중인 게임의 페이즈 타이머가 다시 걸리는지 확인한다.
 *
 * 재시작 흉내: 저장소(CopyingGameRepository, Redis처럼 JSON만 남음)는 그대로 두고,
 * 스케줄러·GameFlowService·GameTimerRecovery를 새로 만든다. 예전 스케줄러에 걸린 타이머는 버린다(실행하지 않는다).
 */
class GameTimerRecoveryTest {

    private static final Instant NOW = Instant.parse("2026-10-01T12:00:00Z");
    // 밤 30, 결과 5, 낮 60, 투표 30, 처형 5. 종료 후 60초 보관, 연결 끊김 검사 끔, 10일 무사망 시 취소
    private static final GamePhaseProperties PROPS = new GamePhaseProperties(30, 5, 60, 30, 5, 60, 0, 5, 10);

    private MutableClock clock;
    private CopyingGameRepository repository;
    private String gameId;

    @BeforeEach
    void setUp() {
        clock = new MutableClock(NOW);
        repository = new CopyingGameRepository();
        gameId = repository.save(new Game("1", List.of(
                new GamePlayer(1L, "해적", TestRoles.RAIDER),
                new GamePlayer(2L, "선장", TestRoles.CAPTAIN),
                new GamePlayer(3L, "선원3", TestRoles.SAILOR),
                new GamePlayer(4L, "선원4", TestRoles.SAILOR),
                new GamePlayer(5L, "선원5", TestRoles.SAILOR)), true)).getGameId();
    }

    /** 서버 한 대 분량: 스케줄러 + 게임 진행 + 타이머 복구 */
    private record Server(ManualTaskScheduler scheduler, GameFlowService flow, GameTimerRecovery recovery) {
    }

    private Server startServer() {
        ManualTaskScheduler scheduler = new ManualTaskScheduler(clock);
        GameFlowService flow = TestGameFlows.create(repository, new NightActionResolver(new Random(0)),
                new VoteResolver(), new WinConditionChecker(), scheduler, PROPS, clock, event -> { },
                new LocalPlayerActivityTracker());
        GameTimerRecovery recovery = new GameTimerRecovery(repository, new LocalGameLock(),
                new LocalGameTimer(scheduler, () -> flow), clock);
        return new Server(scheduler, flow, recovery);
    }

    private Game stored() {
        return repository.findById(gameId).orElseThrow();
    }

    @Test
    void 재시작_후_남은_시간이_지나면_다음_페이즈로_넘어간다() {
        Server before = startServer();
        before.flow().begin(gameId);                         // 밤: NOW + 30초에 끝남
        clock.advance(Duration.ofSeconds(10));               // 10초 뒤 서버가 꺼짐 (before의 타이머는 사라짐)

        Server after = startServer();
        assertThat(after.recovery().recoverAll()).isEqualTo(1);

        after.scheduler().advance(Duration.ofSeconds(19));
        after.scheduler().runDue();
        assertThat(stored().getPhase()).isEqualTo(GamePhase.NIGHT);          // 아직 1초 남음
        after.scheduler().advance(Duration.ofSeconds(1));
        after.scheduler().runDue();
        assertThat(stored().getPhase()).isEqualTo(GamePhase.NIGHT_RESULT);   // 원래 끝나는 시각에 넘어감
    }

    @Test
    void 꺼져_있는_동안_끝날_시각이_지났으면_바로_넘어간다() {
        Server before = startServer();
        before.flow().begin(gameId);
        clock.advance(Duration.ofSeconds(120));              // 밤이 끝날 시각(30초)을 지나서 다시 켜짐

        Server after = startServer();
        after.recovery().recoverAll();
        after.scheduler().runDue();

        assertThat(stored().getPhase()).isEqualTo(GamePhase.NIGHT_RESULT);
    }

    @Test
    void 복구_없이_재시작하면_게임이_멈춘다() {
        Server before = startServer();
        before.flow().begin(gameId);

        Server after = startServer();                        // recoverAll을 부르지 않음
        after.scheduler().advance(Duration.ofSeconds(300));
        after.scheduler().runDue();

        assertThat(stored().getPhase()).isEqualTo(GamePhase.NIGHT);          // 이 작업이 필요한 이유
    }

    @Test
    void 타이머가_이미_걸려_있어도_페이즈는_한_번만_넘어간다() {
        Server server = startServer();
        server.flow().begin(gameId);
        long version = stored().getPhaseVersion();

        server.recovery().recoverAll();                      // 같은 페이즈에 타이머가 두 번 걸림
        server.scheduler().advance(Duration.ofSeconds(30));
        server.scheduler().runDue();

        assertThat(stored().getPhase()).isEqualTo(GamePhase.NIGHT_RESULT);
        assertThat(stored().getPhaseVersion()).isEqualTo(version + 1);       // 두 번째 타이머는 버전이 달라 무시됨
    }

    @Test
    void 시작_전_게임은_타이머를_걸지_않는다() {
        Server server = startServer();                       // begin을 부르지 않은 게임 (phase = null)

        assertThat(server.recovery().recoverAll()).isZero();
    }

    @Test
    void 진행_중인_게임이_없으면_아무것도_하지_않는다() {
        repository.delete(gameId);
        Server server = startServer();

        assertThat(server.recovery().recoverAll()).isZero();
    }
}
