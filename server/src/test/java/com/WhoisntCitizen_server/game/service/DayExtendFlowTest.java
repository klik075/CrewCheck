package com.WhoisntCitizen_server.game.service;

import com.WhoisntCitizen_server.common.config.GamePhaseProperties;
import com.WhoisntCitizen_server.common.event.RoomNoticeEvent;
import com.WhoisntCitizen_server.common.exception.GameRuleException;
import com.WhoisntCitizen_server.game.activity.LocalPlayerActivityTracker;
import com.WhoisntCitizen_server.game.dto.DayExtendResponse;
import com.WhoisntCitizen_server.game.dto.GameStateResponse;
import com.WhoisntCitizen_server.game.entity.Game;
import com.WhoisntCitizen_server.game.entity.GamePhase;
import com.WhoisntCitizen_server.game.entity.GamePlayer;
import com.WhoisntCitizen_server.game.event.GameStateChangedEvent;
import com.WhoisntCitizen_server.game.lock.LocalGameLock;
import com.WhoisntCitizen_server.game.repository.ChangeEventGameRepository;
import com.WhoisntCitizen_server.game.result.GameResultRecorder;
import com.WhoisntCitizen_server.game.scheduling.GameTimer;
import com.WhoisntCitizen_server.game.scheduling.LocalGameTimer;
import com.WhoisntCitizen_server.game.scheduling.SchedulerDeferredEventPublisher;
import com.WhoisntCitizen_server.night.service.NightActionResolver;
import com.WhoisntCitizen_server.night.service.NightService;
import com.WhoisntCitizen_server.support.CopyingGameRepository;
import com.WhoisntCitizen_server.support.ManualTaskScheduler;
import com.WhoisntCitizen_server.support.MutableClock;
import com.WhoisntCitizen_server.support.TestRoles;
import com.WhoisntCitizen_server.vote.service.VoteResolver;
import org.junit.jupiter.api.BeforeEach;
import org.junit.jupiter.api.Test;

import java.time.Duration;
import java.time.Instant;
import java.util.ArrayList;
import java.util.List;
import java.util.Random;

import static org.assertj.core.api.Assertions.assertThat;
import static org.assertj.core.api.Assertions.assertThatThrownBy;
import static org.mockito.Mockito.mock;

/**
 * 낮 토론 시간 연장 (POST /games/{id}/day/extend → GameService.extendDay → GameFlowService.extendDay).
 *  - 마감(phaseEndsAt)만 늦추고 phaseVersion은 그대로 둔다
 *  - 같은 버전의 페이즈 타이머를 늘어난 마감으로 다시 예약한다
 *  - 옛 예약이 이미 실행에 들어갔어도(Redis 확인 작업이 먼저 가져간 경우) onPhaseTimeout이 마감 전이면 넘기지 않고 다시 예약한다
 *  - 저장해서 변경 신호(GameStateChangedEvent → game.changed)가 나가고, 방 채팅으로 알린다
 *
 * 저장소는 CopyingGameRepository(저장 시점 복사본)를 ChangeEventGameRepository로 감싼다. 그래서 검증은 항상 stored()로 하고,
 * 저장을 빠뜨리면 늘어난 마감이 stored()에 없어 실패한다.
 */
class DayExtendFlowTest {

    private static final Instant NOW = Instant.parse("2026-10-01T12:00:00Z");
    // 밤 30, 결과 5, 낮 60, 투표 30, 처형 5. 종료 후 60초 보관, 연결 끊김 검사 끔, 10일 무사망 시 취소, 연장 10초
    private static final GamePhaseProperties PROPS = new GamePhaseProperties(30, 5, 60, 30, 5, 60, 0, 5, 10, 10);
    private static final int UNTIL_DAY = 35;                           // 밤 30 + 결과 5
    private static final Instant DAY_ENDS = NOW.plusSeconds(35 + 60);  // 늘리기 전 낮 마감

    /** 페이즈 타이머 예약 한 건 */
    private record Scheduled(long phaseVersion, Instant at) {
    }

    private final List<Object> events = new ArrayList<>();
    private final List<Scheduled> phaseSchedules = new ArrayList<>();
    private MutableClock clock;
    private ManualTaskScheduler scheduler;
    private CopyingGameRepository stored;
    private GameFlowService flow;
    private GameService gameService;
    private NightService nightService;
    private String gameId;

    @BeforeEach
    void setUp() {
        clock = new MutableClock(NOW);
        scheduler = new ManualTaskScheduler(clock);
        stored = new CopyingGameRepository();
        ChangeEventGameRepository repository = new ChangeEventGameRepository(stored, events::add);
        LocalGameLock lock = new LocalGameLock();

        // 예약을 기록해 두고, 실제 예약은 LocalGameTimer(같은 대상은 마지막 예약만 실행)에 맡긴다
        GameFlowService[] holder = new GameFlowService[1];
        LocalGameTimer localTimer = new LocalGameTimer(scheduler, () -> holder[0]);
        GameTimer recordingTimer = new GameTimer() {
            @Override
            public void schedulePhaseTimeout(String id, long phaseVersion, Instant at) {
                phaseSchedules.add(new Scheduled(phaseVersion, at));
                localTimer.schedulePhaseTimeout(id, phaseVersion, at);
            }

            @Override
            public void scheduleCleanup(String id, Instant at) {
                localTimer.scheduleCleanup(id, at);
            }
        };
        holder[0] = new GameFlowService(repository, new NightActionResolver(new Random(0)), new VoteResolver(),
                new WinConditionChecker(), recordingTimer,
                new SchedulerDeferredEventPublisher(scheduler, clock, events::add),
                lock, new LocalPlayerActivityTracker(), PROPS, clock, events::add, GameResultRecorder.NONE);
        flow = holder[0];
        gameService = new GameService(repository, mock(RoleAssigner.class), flow, lock,
                new LocalPlayerActivityTracker(), clock);
        nightService = new NightService(repository, flow, lock, clock);

        // 1: 해적, 2: 선장, 3~5: 선원
        gameId = repository.save(new Game("1", List.of(
                new GamePlayer(1L, "해적", TestRoles.RAIDER),
                new GamePlayer(2L, "선장", TestRoles.CAPTAIN),
                new GamePlayer(3L, "선원3", TestRoles.SAILOR),
                new GamePlayer(4L, "선원4", TestRoles.SAILOR),
                new GamePlayer(5L, "선원5", TestRoles.SAILOR)), true)).getGameId();
    }

    private Game stored() {
        return stored.findById(gameId).orElseThrow();
    }

    private void advance(int seconds) {
        scheduler.advance(Duration.ofSeconds(seconds));
        scheduler.runDue();
    }

    private List<String> notices() {
        return events.stream()
                .filter(RoomNoticeEvent.class::isInstance)
                .map(e -> ((RoomNoticeEvent) e).message())
                .toList();
    }

    /** 첫 낮까지 진행한다. 낮의 phaseVersion을 돌려준다 */
    private long startDay() {
        flow.begin(gameId);
        advance(UNTIL_DAY);
        assertThat(stored().getPhase()).isEqualTo(GamePhase.DAY);
        assertThat(stored().getPhaseEndsAt()).isEqualTo(DAY_ENDS);
        return stored().getPhaseVersion();
    }

    // ---------- 연장 ----------

    @Test
    void 늘리면_마감만_늦춰지고_phaseVersion은_그대로다() {
        long dayVersion = startDay();
        advance(5);
        events.clear();

        DayExtendResponse response = gameService.extendDay(gameId, 2L);

        assertThat(response.accepted()).isTrue();
        assertThat(response.phase()).isEqualTo(GamePhase.DAY);
        assertThat(response.phaseVersion()).isEqualTo(dayVersion);
        assertThat(response.phaseEndsAt()).isEqualTo(DAY_ENDS.plusSeconds(10));
        assertThat(response.addedSeconds()).isEqualTo(10);
        assertThat(response.extendedCount()).isEqualTo(1);

        // 저장됐다 (이후 상태 조회에 늘어난 마감이 보이고, 버전은 그대로)
        assertThat(stored().getPhaseEndsAt()).isEqualTo(DAY_ENDS.plusSeconds(10));
        assertThat(stored().hasExtendedDay(2L)).isTrue();
        GameStateResponse state = gameService.getState(gameId);
        assertThat(state.phaseEndsAt()).isEqualTo(DAY_ENDS.plusSeconds(10));
        assertThat(state.phaseVersion()).isEqualTo(dayVersion);
    }

    @Test
    void 늘리면_변경_신호와_채팅_안내가_나간다() {
        long dayVersion = startDay();
        events.clear();

        gameService.extendDay(gameId, 3L);

        assertThat(events).contains(new GameStateChangedEvent(gameId, "1", dayVersion));
        assertThat(notices()).containsExactly("선원3님이 토론 시간을 10초 늘렸습니다.");
    }

    @Test
    void 같은_사람이_다시_보내면_시간은_그대로이고_늘어난_초는_0이다() {
        startDay();
        gameService.extendDay(gameId, 2L);
        int schedulesBefore = phaseSchedules.size();
        events.clear();

        DayExtendResponse again = gameService.extendDay(gameId, 2L);

        assertThat(again.accepted()).isTrue();
        assertThat(again.addedSeconds()).isZero();
        assertThat(again.extendedCount()).isEqualTo(1);
        assertThat(again.phaseEndsAt()).isEqualTo(DAY_ENDS.plusSeconds(10));
        assertThat(stored().getPhaseEndsAt()).isEqualTo(DAY_ENDS.plusSeconds(10));
        assertThat(notices()).isEmpty();                      // 안내도 다시 하지 않는다
        assertThat(phaseSchedules).hasSize(schedulesBefore);   // 타이머도 다시 잡지 않는다
    }

    @Test
    void 여러_사람이_늘리면_한_사람씩_더해진다() {
        startDay();

        gameService.extendDay(gameId, 2L);
        DayExtendResponse second = gameService.extendDay(gameId, 4L);

        assertThat(second.addedSeconds()).isEqualTo(10);
        assertThat(second.extendedCount()).isEqualTo(2);
        assertThat(second.phaseEndsAt()).isEqualTo(DAY_ENDS.plusSeconds(20));
        assertThat(stored().getPhaseEndsAt()).isEqualTo(DAY_ENDS.plusSeconds(20));
    }

    @Test
    void 낮이_아니면_늘릴_수_없다() {
        flow.begin(gameId);                                    // 밤
        Instant nightEnds = stored().getPhaseEndsAt();
        int schedulesBefore = phaseSchedules.size();

        assertThatThrownBy(() -> gameService.extendDay(gameId, 2L)).isInstanceOf(GameRuleException.class);

        assertThat(stored().getPhaseEndsAt()).isEqualTo(nightEnds);
        assertThat(phaseSchedules).hasSize(schedulesBefore);
    }

    @Test
    void 사망자는_늘릴_수_없다() {
        flow.begin(gameId);
        nightService.submitAction(gameId, 1L, 3L);             // 해적 → 선원3 습격
        advance(UNTIL_DAY);
        assertThat(stored().getPhase()).isEqualTo(GamePhase.DAY);
        assertThat(stored().getPlayer(3L).isAlive()).isFalse();

        assertThatThrownBy(() -> gameService.extendDay(gameId, 3L)).isInstanceOf(GameRuleException.class);

        assertThat(stored().getPhaseEndsAt()).isEqualTo(DAY_ENDS);
        assertThat(stored().dayExtendCount()).isZero();
    }

    // ---------- 타이머 ----------

    @Test
    void 같은_버전의_타이머를_늘어난_마감으로_다시_예약해_원래_마감에는_넘어가지_않는다() {
        long dayVersion = startDay();
        advance(5);

        gameService.extendDay(gameId, 2L);

        assertThat(phaseSchedules).last().isEqualTo(new Scheduled(dayVersion, DAY_ENDS.plusSeconds(10)));
        advance(55);                                           // 원래 낮 마감 (옛 예약은 실행되지 않음)
        assertThat(stored().getPhase()).isEqualTo(GamePhase.DAY);
        advance(10);                                           // 늘어난 마감
        Game game = stored();
        assertThat(game.getPhase()).isEqualTo(GamePhase.VOTE);
        assertThat(game.getPhaseVersion()).isEqualTo(dayVersion + 1);
        assertThat(game.getPhaseEndsAt()).isEqualTo(DAY_ENDS.plusSeconds(10 + 30));
    }

    @Test
    void 옛_예약이_이미_실행에_들어갔어도_마감_전이면_넘기지_않고_다시_예약한다() {
        long dayVersion = startDay();
        gameService.extendDay(gameId, 2L);
        advance(60);                                           // 원래 낮 마감
        int schedulesBefore = phaseSchedules.size();

        // Redis 확인 작업이 연장 전에 옛 예약을 가져가 원래 마감에 실행한 상황
        flow.onPhaseTimeout(gameId, dayVersion);

        assertThat(stored().getPhase()).isEqualTo(GamePhase.DAY);
        assertThat(stored().getPhaseVersion()).isEqualTo(dayVersion);
        assertThat(phaseSchedules).hasSize(schedulesBefore + 1);
        assertThat(phaseSchedules).last().isEqualTo(new Scheduled(dayVersion, DAY_ENDS.plusSeconds(10)));

        advance(10);                                           // 늘어난 마감
        assertThat(stored().getPhase()).isEqualTo(GamePhase.VOTE);
    }

    @Test
    void 마감이_지난_뒤의_타이머는_그대로_넘어간다() {
        long dayVersion = startDay();
        gameService.extendDay(gameId, 2L);
        clock.set(DAY_ENDS.plusSeconds(10));                   // 늘어난 마감에 딱 맞춰 (예약 실행 전)

        flow.onPhaseTimeout(gameId, dayVersion);

        assertThat(stored().getPhase()).isEqualTo(GamePhase.VOTE);
    }

    @Test
    void 마감_비교는_타이머와_같은_밀리초_단위다() {
        clock.set(NOW.plusNanos(700_000));                     // 마감에 밀리초보다 작은 값(0.7ms)이 붙게 한다
        long dayVersion = startDayFromNow();
        gameService.extendDay(gameId, 2L);
        Instant endsAt = stored().getPhaseEndsAt();

        clock.set(endsAt.minusMillis(1));                      // 1ms 전: 아직 마감 전
        flow.onPhaseTimeout(gameId, dayVersion);
        assertThat(stored().getPhase()).isEqualTo(GamePhase.DAY);

        // Redis는 밀리초 점수로 예약을 꺼내므로 같은 밀리초 안(0.5ms 전)에 불릴 수 있다. 이때는 마감으로 본다
        clock.set(endsAt.minusNanos(500_000));
        flow.onPhaseTimeout(gameId, dayVersion);
        assertThat(stored().getPhase()).isEqualTo(GamePhase.VOTE);
    }

    /** startDay와 같지만 마감을 지금 시계 기준으로 확인한다 */
    private long startDayFromNow() {
        Instant start = clock.instant();
        flow.begin(gameId);
        advance(UNTIL_DAY);
        assertThat(stored().getPhase()).isEqualTo(GamePhase.DAY);
        assertThat(stored().getPhaseEndsAt()).isEqualTo(start.plusSeconds(UNTIL_DAY + 60));
        return stored().getPhaseVersion();
    }

    // ---------- 넘기기와의 관계 ----------

    @Test
    void 늘렸어도_전원이_넘기면_바로_투표로_넘어간다() {
        startDay();
        gameService.extendDay(gameId, 2L);
        Instant now = clock.instant();

        for (long id = 1; id <= 5; id++) {
            gameService.skipDay(gameId, id);
        }

        Game game = stored();
        assertThat(game.getPhase()).isEqualTo(GamePhase.VOTE);
        assertThat(game.getPhaseEndsAt()).isEqualTo(now.plusSeconds(30));
    }
}
