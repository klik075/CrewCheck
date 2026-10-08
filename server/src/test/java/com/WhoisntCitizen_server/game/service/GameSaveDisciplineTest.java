package com.WhoisntCitizen_server.game.service;

import com.WhoisntCitizen_server.common.config.GamePhaseProperties;
import com.WhoisntCitizen_server.common.exception.GameRuleException;
import com.WhoisntCitizen_server.game.activity.LocalPlayerActivityTracker;
import com.WhoisntCitizen_server.game.entity.Game;
import com.WhoisntCitizen_server.game.entity.GameEndReason;
import com.WhoisntCitizen_server.game.entity.GamePhase;
import com.WhoisntCitizen_server.game.entity.GamePlayer;
import com.WhoisntCitizen_server.game.entity.Winner;
import com.WhoisntCitizen_server.game.lock.LocalGameLock;
import com.WhoisntCitizen_server.night.entity.NightResult;
import com.WhoisntCitizen_server.night.service.NightActionResolver;
import com.WhoisntCitizen_server.night.service.NightService;
import com.WhoisntCitizen_server.support.CopyingGameRepository;
import com.WhoisntCitizen_server.support.ManualTaskScheduler;
import com.WhoisntCitizen_server.support.MutableClock;
import com.WhoisntCitizen_server.support.TestGameFlows;
import com.WhoisntCitizen_server.support.TestRoles;
import com.WhoisntCitizen_server.vote.service.VoteResolver;
import com.WhoisntCitizen_server.vote.service.VoteService;
import org.junit.jupiter.api.BeforeEach;
import org.junit.jupiter.api.Test;

import java.time.Duration;
import java.time.Instant;
import java.util.ArrayList;
import java.util.List;
import java.util.Map;
import java.util.Random;

import static org.assertj.core.api.Assertions.assertThat;
import static org.assertj.core.api.Assertions.assertThatThrownBy;
import static org.mockito.ArgumentMatchers.any;
import static org.mockito.Mockito.mock;
import static org.mockito.Mockito.when;

/**
 * 저장 누락 점검: 상태를 바꾸는 모든 경로가 save()로 끝나는지 확인한다.
 *
 * CopyingGameRepository는 Redis처럼 save() 시점의 복사본만 돌려준다.
 * 그래서 검증은 항상 저장소에서 다시 꺼낸 Game(stored())으로 한다. 처음 만든 Game 객체를 들고 있지 않는다.
 * 어떤 경로에서 save()를 빼먹으면, 그 경로에서 바꾼 내용이 stored()에 없어서 테스트가 실패한다.
 */
class GameSaveDisciplineTest {

    private static final Instant NOW = Instant.parse("2026-10-01T12:00:00Z");

    // 밤 30, 결과 5, 낮 60, 투표 30, 처형 5. 종료 후 60초 보관, 10초 동안 요청이 없으면 이탈, 10일 무사망 시 취소
    private static final GamePhaseProperties PROPS = new GamePhaseProperties(30, 5, 60, 30, 5, 60, 10, 5, 10);
    private static final int UNTIL_VOTE = 95;        // 밤 30 + 결과 5 + 낮 60
    private static final int UNTIL_VOTE_ENDS = 125;  // + 투표 30

    private MutableClock clock;
    private ManualTaskScheduler scheduler;
    private CopyingGameRepository repository;
    private LocalPlayerActivityTracker tracker;
    private final List<Object> events = new ArrayList<>();
    private GameFlowService flow;
    private NightService nightService;
    private VoteService voteService;
    private String gameId;

    @BeforeEach
    void setUp() {
        clock = new MutableClock(NOW);
        scheduler = new ManualTaskScheduler(clock);
        repository = new CopyingGameRepository();
        tracker = new LocalPlayerActivityTracker();
        useFlow(newFlow(new VoteResolver(), PROPS));
        // 1: 해적, 2: 선장, 3~5: 선원
        gameId = saveNewGame(
                new GamePlayer(1L, "해적", TestRoles.RAIDER),
                new GamePlayer(2L, "선장", TestRoles.CAPTAIN),
                new GamePlayer(3L, "선원3", TestRoles.SAILOR),
                new GamePlayer(4L, "선원4", TestRoles.SAILOR),
                new GamePlayer(5L, "선원5", TestRoles.SAILOR));
    }

    private GameFlowService newFlow(VoteResolver voteResolver, GamePhaseProperties props) {
        return TestGameFlows.create(repository, new NightActionResolver(new Random(0)), voteResolver,
                new WinConditionChecker(), scheduler, props, clock, events::add, tracker);
    }

    private void useFlow(GameFlowService newFlow) {
        flow = newFlow;
        nightService = new NightService(repository, flow, new LocalGameLock(), clock);
        voteService = new VoteService(repository, flow, new LocalGameLock());
    }

    /** GameService.start처럼 새 게임을 저장하고 id만 돌려준다. (만든 객체는 테스트에서 쓰지 않는다) */
    private String saveNewGame(GamePlayer... players) {
        return repository.save(new Game("1", List.of(players), true)).getGameId();
    }

    /** 저장소에 실제로 남아 있는 상태 */
    private Game stored() {
        return repository.findById(gameId).orElseThrow();
    }

    private void advance(int seconds) {
        scheduler.advance(Duration.ofSeconds(seconds));
        scheduler.runDue();
    }

    // ---------- 페이즈 전환 (moveTo) ----------

    @Test
    void 게임_시작_후_첫_밤이_저장된다() {
        flow.begin(gameId);

        Game game = stored();
        assertThat(game.getPhase()).isEqualTo(GamePhase.NIGHT);
        assertThat(game.getDay()).isEqualTo(1);
        assertThat(game.getPhaseEndsAt()).isEqualTo(NOW.plusSeconds(30));
    }

    @Test
    void 타이머로_넘어간_페이즈가_저장된다() {
        flow.begin(gameId);

        advance(30);
        assertThat(stored().getPhase()).isEqualTo(GamePhase.NIGHT_RESULT);
        assertThat(stored().getLastNightResult()).isNotNull();
        advance(5);
        assertThat(stored().getPhase()).isEqualTo(GamePhase.DAY);
        advance(60);
        assertThat(stored().getPhase()).isEqualTo(GamePhase.VOTE);
    }

    // ---------- 밤 능력 (NightService) ----------

    @Test
    void 밤_능력_제출이_저장된다() {
        flow.begin(gameId);

        nightService.submitAction(gameId, 1L, 3L);

        Game game = stored();
        assertThat(game.getNightActions()).containsOnlyKeys(1L);
        assertThat(game.getNightActions().get(1L).targetId()).isEqualTo(3L);
        assertThat(game.getPhase()).isEqualTo(GamePhase.NIGHT);
    }

    @Test
    void 능력_넘기기가_저장된다() {
        flow.begin(gameId);
        nightService.submitAction(gameId, 1L, 3L);

        nightService.skipAction(gameId, 1L);
        assertThat(stored().getNightActions()).isEmpty();

        // 넘기기가 저장됐어야 선장 제출로 "전원 제출"이 되어 바로 판정된다
        nightService.submitAction(gameId, 2L, 1L);
        assertThat(stored().getPhase()).isEqualTo(GamePhase.NIGHT_RESULT);
    }

    @Test
    void 전원_제출로_끝난_밤_판정_결과가_저장된다() {
        flow.begin(gameId);

        nightService.submitAction(gameId, 1L, 3L);   // 해적 → 선원3 습격
        nightService.submitAction(gameId, 2L, 1L);   // 선장 → 해적 조사 (전원 제출 → 판정)

        Game game = stored();
        assertThat(game.getPhase()).isEqualTo(GamePhase.NIGHT_RESULT);
        assertThat(game.getPlayer(3L).isAlive()).isFalse();            // 판정 안의 kill()
        assertThat(game.getLastNightResult().deaths())
                .extracting(NightResult.Death::playerId).containsExactly(3L);    // setLastNightResult
        assertThat(game.getLastNightResult().reportsFor(2L)).isNotEmpty();
        assertThat(game.daysWithoutDeath()).isZero();                  // recordDeath
    }

    @Test
    void 앵무새_접선이_저장된다() {
        // 1: 해적, 2: 앵무새, 3: 선장, 4~5: 선원
        gameId = saveNewGame(
                new GamePlayer(1L, "해적", TestRoles.RAIDER),
                new GamePlayer(2L, "앵무새", TestRoles.PARROT),
                new GamePlayer(3L, "선장", TestRoles.CAPTAIN),
                new GamePlayer(4L, "선원4", TestRoles.SAILOR),
                new GamePlayer(5L, "선원5", TestRoles.SAILOR));
        flow.begin(gameId);

        nightService.submitAction(gameId, 2L, 1L);   // 앵무새 → 해적 지목 = 접선

        Game game = stored();
        assertThat(game.getPlayer(2L).isContacted()).isTrue();          // markContacted
        assertThat(game.getPlayer(2L).getContactedAt()).isEqualTo(NOW);
        // 접선한 밤에는 행동이 고정된다 (lockedActors도 저장됐는지)
        assertThatThrownBy(() -> nightService.submitAction(gameId, 2L, 4L))
                .isInstanceOf(GameRuleException.class);
    }

    // ---------- 투표 → 처형 → 승리 검사 (VoteService) ----------

    @Test
    void 투표가_저장된다() {
        flow.begin(gameId);
        advance(UNTIL_VOTE);

        voteService.vote(gameId, 2L, 1L, true);

        assertThat(stored().getVotes()).isEqualTo(Map.of(2L, 1L));
    }

    @Test
    void 처형_결과와_다음_날_밤이_저장된다() {
        flow.begin(gameId);
        advance(UNTIL_VOTE);

        voteService.vote(gameId, 1L, 3L, true);
        voteService.vote(gameId, 2L, 3L, true);
        voteService.vote(gameId, 3L, 1L, true);
        voteService.vote(gameId, 4L, 3L, true);
        voteService.vote(gameId, 5L, 3L, true);            // 전원 투표 → 처형

        Game game = stored();
        assertThat(game.getPhase()).isEqualTo(GamePhase.EXECUTION);
        assertThat(game.getPlayer(3L).isAlive()).isFalse();
        assertThat(game.getLastExecutionResult().executedPlayerId()).isEqualTo(3L);
        assertThat(game.daysWithoutDeath()).isZero();

        advance(5);
        assertThat(stored().getPhase()).isEqualTo(GamePhase.NIGHT);
        assertThat(stored().getDay()).isEqualTo(2);

        // 표는 밤이 아니라 다음 투표(VOTE)에 들어갈 때 비운다 (Game.changePhase). 비운 상태도 저장돼야 한다
        advance(UNTIL_VOTE);
        assertThat(stored().getPhase()).isEqualTo(GamePhase.VOTE);
        assertThat(stored().getDay()).isEqualTo(2);
        assertThat(stored().getVotes()).isEmpty();
    }

    @Test
    void 승리로_끝난_게임이_저장되고_보관_시간이_지나면_삭제된다() {
        flow.begin(gameId);
        advance(UNTIL_VOTE);

        voteService.vote(gameId, 1L, 2L, true);
        voteService.vote(gameId, 2L, 1L, true);
        voteService.vote(gameId, 3L, 1L, true);
        voteService.vote(gameId, 4L, 1L, true);
        voteService.vote(gameId, 5L, 1L, true);            // 해적 처형 → 선원 승리

        Game game = stored();
        assertThat(game.isEnded()).isTrue();                            // end()
        assertThat(game.getWinner()).isEqualTo(Winner.CREW);
        assertThat(game.getWinnerIds()).containsExactlyInAnyOrder(2L, 3L, 4L, 5L);
        assertThat(game.getPlayer(1L).isAlive()).isFalse();

        advance(60);
        assertThat(repository.findById(gameId)).isEmpty();
    }

    // ---------- 연결 끊김 (handleDepartures) ----------

    /** 이탈 기준(10초)을 넘기면서 keep에 있는 사람만 요청을 계속 보낸다. */
    private void passInactiveTimeout(Long... keep) {
        advance(11);
        for (Long id : keep) {
            tracker.touch(gameId, id, clock.instant());
        }
    }

    @Test
    void 연결이_끊긴_플레이어의_사망과_이탈이_저장된다() {
        flow.begin(gameId);
        passInactiveTimeout(1L, 2L, 3L, 4L);

        flow.checkInactivePlayers(gameId);

        Game game = stored();
        assertThat(game.getPlayer(5L).isAlive()).isFalse();             // depart() 안의 kill()
        assertThat(game.getPlayer(5L).isDeparted()).isTrue();           // markDeparted()
        assertThat(game.getPhase()).isEqualTo(GamePhase.NIGHT);
    }

    @Test
    void 끊긴_사람만_투표하지_않았을_때_바로_판정한_결과가_저장된다() {
        flow.begin(gameId);
        advance(UNTIL_VOTE);
        voteService.vote(gameId, 1L, 2L, true);
        voteService.vote(gameId, 2L, 1L, true);
        voteService.vote(gameId, 3L, 1L, true);
        voteService.vote(gameId, 4L, 1L, true);
        passInactiveTimeout(1L, 2L, 3L, 4L);

        flow.checkInactivePlayers(gameId);           // 5 이탈 → 남은 전원 투표 완료 → 처형 → 승리

        Game game = stored();
        assertThat(game.getPlayer(5L).isDeparted()).isTrue();
        assertThat(game.getLastExecutionResult().executedPlayerId()).isEqualTo(1L);
        assertThat(game.getWinner()).isEqualTo(Winner.CREW);
    }

    // ---------- 게임 취소 (cancel) ----------

    @Test
    void 살아_있는_플레이어가_모두_끊겨_취소된_상태가_저장된다() {
        flow.begin(gameId);
        passInactiveTimeout();

        flow.checkInactivePlayers(gameId);

        Game game = stored();
        assertThat(game.isEnded()).isTrue();
        assertThat(game.getEndReason()).isEqualTo(GameEndReason.CANCELLED_ALL_DISCONNECTED);
    }

    @Test
    void 무사망으로_취소된_상태가_저장된다() {
        useFlow(newFlow(new VoteResolver(), new GamePhaseProperties(30, 5, 60, 30, 5, 60, 10, 5, 1)));
        flow.begin(gameId);

        advance(UNTIL_VOTE_ENDS);                    // 1일차에 아무도 안 죽음 → 투표 결과 직후 취소

        assertThat(stored().getEndReason()).isEqualTo(GameEndReason.CANCELLED_NO_DEATHS);
    }

    @Test
    void 판정_중_예외로_취소된_상태가_저장된다() {
        VoteResolver broken = mock(VoteResolver.class);
        when(broken.resolve(any())).thenThrow(new IllegalStateException("판정 버그"));
        useFlow(newFlow(broken, PROPS));
        flow.begin(gameId);

        advance(UNTIL_VOTE_ENDS);

        assertThat(stored().getEndReason()).isEqualTo(GameEndReason.CANCELLED_ERROR);
    }
}
