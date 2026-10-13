package com.WhoisntCitizen_server.game.service;

import com.WhoisntCitizen_server.common.config.GamePhaseProperties;
import com.WhoisntCitizen_server.common.event.PirateNoticeEvent;
import com.WhoisntCitizen_server.common.event.RoomNoticeEvent;
import com.WhoisntCitizen_server.game.activity.LocalPlayerActivityTracker;
import com.WhoisntCitizen_server.game.entity.Game;
import com.WhoisntCitizen_server.game.entity.GameEndReason;
import com.WhoisntCitizen_server.game.entity.GamePhase;
import com.WhoisntCitizen_server.game.entity.GamePlayer;
import com.WhoisntCitizen_server.game.entity.Winner;
import com.WhoisntCitizen_server.game.event.CancelledGameExpiredEvent;
import com.WhoisntCitizen_server.game.event.GameEndedEvent;
import com.WhoisntCitizen_server.game.event.PlayersDepartedEvent;
import com.WhoisntCitizen_server.game.repository.InMemoryGameRepository;
import com.WhoisntCitizen_server.jobs.domain.ActionCode;
import com.WhoisntCitizen_server.jobs.domain.Faction;
import com.WhoisntCitizen_server.jobs.domain.RoleDefinition;
import com.WhoisntCitizen_server.night.service.NightActionResolver;
import com.WhoisntCitizen_server.support.ManualTaskScheduler;
import com.WhoisntCitizen_server.support.TestGameFlows;
import com.WhoisntCitizen_server.support.MutableClock;
import com.WhoisntCitizen_server.vote.service.VoteResolver;
import org.junit.jupiter.api.BeforeEach;
import org.junit.jupiter.api.Test;

import java.time.Duration;
import java.time.Instant;
import java.util.ArrayList;
import java.util.List;
import java.util.Map;
import java.util.Random;

import static org.assertj.core.api.Assertions.assertThat;
import static org.assertj.core.api.Assertions.assertThatCode;
import static org.mockito.ArgumentMatchers.any;
import static org.mockito.Mockito.mock;
import static org.mockito.Mockito.when;

/**
 * 끝나지 않는 게임 방지: 연결 끊김, 사망자 없는 날 연속, 페이즈 전환 중 예외.
 * 실제 GameFlowService를 수동 스케줄러/시계로 돌려 페이즈 타이머까지 확인한다.
 */
class GameFlowServiceEndlessGameTest {

    private static final RoleDefinition RAIDER =
            new RoleDefinition("PIRATE_RAIDER", "해적", Faction.PIRATE, ActionCode.SELECT_ATTACK_TARGET);
    private static final RoleDefinition CAPTAIN =
            new RoleDefinition("CREW_CAPTAIN", "선장", Faction.CREW, ActionCode.INVESTIGATE_FACTION);
    private static final RoleDefinition SAILOR =
            new RoleDefinition("CREW_SAILOR", "선원", Faction.CREW, null);

    private static final Instant NOW = Instant.parse("2026-10-01T12:00:00Z");

    // 밤 30, 결과 5, 낮 60, 투표 30, 처형 5 → 하루 130초. 종료 후 60초 보관, 10초 동안 요청이 없으면 이탈, 10일 무사망 시 취소
    private static final GamePhaseProperties PROPS = new GamePhaseProperties(30, 5, 60, 30, 5, 60, 10, 5, 10);
    private static final int DAY_SECONDS = 130;
    private static final int UNTIL_VOTE_ENDS = 125; // 그날 밤 시작부터 투표가 끝날 때까지

    private MutableClock clock;
    private ManualTaskScheduler scheduler;
    private InMemoryGameRepository repository;
    private LocalPlayerActivityTracker tracker;
    private final List<Object> events = new ArrayList<>();
    private GameFlowService flow;
    // 1: 해적, 2: 선장, 3~5: 선원
    private Game game;

    @BeforeEach
    void setUp() {
        clock = new MutableClock(NOW);
        scheduler = new ManualTaskScheduler(clock);
        repository = new InMemoryGameRepository();
        tracker = new LocalPlayerActivityTracker();
        flow = newFlow(new VoteResolver());
        game = newGame();
    }

    private GameFlowService newFlow(VoteResolver voteResolver) {
        return TestGameFlows.create(repository, new NightActionResolver(new Random(0)), voteResolver,
                new WinConditionChecker(), scheduler, PROPS, clock, events::add, tracker);
    }

    private Game newGame() {
        Game g = new Game("1", List.of(
                new GamePlayer(1L, "해적", RAIDER),
                new GamePlayer(2L, "선장", CAPTAIN),
                new GamePlayer(3L, "선원3", SAILOR),
                new GamePlayer(4L, "선원4", SAILOR),
                new GamePlayer(5L, "선원5", SAILOR)), true);
        repository.save(g);
        return g;
    }

    /** id들만 지금 요청을 보낸 것으로 기록한다. */
    private void touch(Long... ids) {
        for (Long id : ids) {
            tracker.touch(game.getGameId(), id, clock.instant());
        }
    }

    /** 10초 이탈 기준을 넘기도록 시간을 보내면서, keep에 있는 사람만 계속 요청을 보낸다. */
    private void passInactiveTimeout(Long... keep) {
        scheduler.advance(Duration.ofSeconds(11));
        touch(keep);
    }

    private <T> List<T> eventsOf(Class<T> type) {
        return events.stream().filter(type::isInstance).map(type::cast).toList();
    }

    private List<String> notices() {
        return eventsOf(RoomNoticeEvent.class).stream().map(RoomNoticeEvent::message).toList();
    }

    // ---------- 연결 끊김 ----------

    @Test
    void 요청이_끊긴_생존자는_사망_처리되고_방에서_빼도록_알린다() {
        flow.begin(game.getGameId());
        passInactiveTimeout(1L, 2L, 3L, 4L);

        flow.checkInactivePlayers(game.getGameId());
        scheduler.runDue();

        GamePlayer left = game.getPlayer(5L);
        assertThat(left.isAlive()).isFalse();
        assertThat(left.isDeparted()).isTrue();
        assertThat(game.getPhase()).isEqualTo(GamePhase.NIGHT);
        assertThat(eventsOf(PlayersDepartedEvent.class))
                .containsExactly(new PlayersDepartedEvent(game.getGameId(), "1", List.of(5L)));
        assertThat(notices()).contains("선원5님의 연결이 끊겨 사망 처리되었습니다.");
    }

    @Test
    void 요청을_계속_보내는_플레이어는_내보내지_않는다() {
        flow.begin(game.getGameId());
        passInactiveTimeout(1L, 2L, 3L, 4L, 5L);

        flow.checkInactivePlayers(game.getGameId());
        scheduler.runDue();

        assertThat(game.getPlayers()).allMatch(GamePlayer::isAlive);
        assertThat(eventsOf(PlayersDepartedEvent.class)).isEmpty();
    }

    @Test
    void 하나뿐인_해적이_끊기면_선원팀이_승리하고_전적에_반영되는_종료가_된다() {
        flow.begin(game.getGameId());
        passInactiveTimeout(2L, 3L, 4L, 5L);

        flow.checkInactivePlayers(game.getGameId());
        scheduler.runDue();

        assertThat(game.isEnded()).isTrue();
        assertThat(game.getWinner()).isEqualTo(Winner.CREW);
        assertThat(game.getEndReason()).isEqualTo(GameEndReason.WIN);
        GameEndedEvent ended = eventsOf(GameEndedEvent.class).get(0);
        assertThat(ended.cancelled()).isFalse();
        assertThat(ended.winner()).isEqualTo(Winner.CREW);
    }

    @Test
    void 살아_있는_플레이어가_모두_끊기면_아무도_죽이지_않고_취소한_뒤_보관_시간이_지나면_방_삭제를_알린다() {
        flow.begin(game.getGameId());
        passInactiveTimeout();

        flow.checkInactivePlayers(game.getGameId());
        scheduler.runDue();

        assertThat(game.isEnded()).isTrue();
        assertThat(game.getEndReason()).isEqualTo(GameEndReason.CANCELLED_ALL_DISCONNECTED);
        assertThat(game.getWinner()).isNull();
        assertThat(game.getPlayers()).allMatch(GamePlayer::isAlive);
        assertThat(eventsOf(PlayersDepartedEvent.class)).isEmpty();
        assertThat(eventsOf(GameEndedEvent.class)).singleElement().satisfies(e -> {
            assertThat(e.cancelled()).isTrue();
            assertThat(e.winner()).isNull();
        });
        assertThat(eventsOf(CancelledGameExpiredEvent.class)).isEmpty(); // 결과 조회 시간 동안은 방을 남긴다

        scheduler.advance(Duration.ofSeconds(60));

        assertThat(repository.findById(game.getGameId())).isEmpty();
        assertThat(eventsOf(CancelledGameExpiredEvent.class))
                .containsExactly(new CancelledGameExpiredEvent(game.getGameId(), "1"));
    }

    // 엇갈린 마지막 요청 시각: 이탈 기준 10초, 검사 주기 5초.
    // 해적(1)은 0초, 선원(2~5)은 3초에 마지막으로 요청하고 모두 끊긴 뒤 11초에 검사하면
    // 기준(1초)을 넘은 사람은 해적뿐이고, 선원은 다음 검사(16초, 기준 6초) 전에 넘는다.

    @Test
    void 함께_끊긴_생존자들이_서로_다른_검사에_걸려도_다음_검사_전에_모두_끊기면_아무도_죽이지_않고_취소한다() {
        flow.begin(game.getGameId());
        scheduler.advance(Duration.ofSeconds(3));
        touch(2L, 3L, 4L, 5L);
        scheduler.advance(Duration.ofSeconds(8));

        flow.checkInactivePlayers(game.getGameId());
        scheduler.runDue();

        // 예전에는 해적만 먼저 사망 처리되어 끊긴 선원 팀이 승리했다
        assertThat(game.getEndReason()).isEqualTo(GameEndReason.CANCELLED_ALL_DISCONNECTED);
        assertThat(game.getWinner()).isNull();
        assertThat(game.getPlayers()).allMatch(GamePlayer::isAlive);
        assertThat(eventsOf(PlayersDepartedEvent.class)).isEmpty();
    }

    @Test
    void 먼저_끊긴_사람과_같은_시각에_끊긴_사람이_있어도_요청을_계속_보내는_생존자가_있으면_취소하지_않는다() {
        flow.begin(game.getGameId());
        scheduler.advance(Duration.ofSeconds(3));
        touch(2L, 3L, 4L, 5L);
        scheduler.advance(Duration.ofSeconds(8));
        touch(2L); // 선장은 계속 접속 중

        flow.checkInactivePlayers(game.getGameId());
        scheduler.runDue();

        assertThat(game.getPlayer(1L).isDeparted()).isTrue();
        assertThat(game.getPlayers().stream().filter(GamePlayer::isDeparted)).hasSize(1); // 3~5는 다음 검사에서
        assertThat(game.getWinner()).isEqualTo(Winner.CREW);
        assertThat(game.getEndReason()).isEqualTo(GameEndReason.WIN);
    }

    @Test
    void 다음_검사_뒤에야_기준을_넘는_생존자가_있으면_취소하지_않고_먼저_끊긴_사람만_처리한다() {
        flow.begin(game.getGameId());
        scheduler.advance(Duration.ofSeconds(6));
        touch(2L, 3L, 4L, 5L); // 선원은 6초: 11초 검사의 다음 기준(6초)보다 오래되지 않았다
        scheduler.advance(Duration.ofSeconds(5));

        flow.checkInactivePlayers(game.getGameId());
        scheduler.runDue();

        assertThat(game.getPlayer(1L).isDeparted()).isTrue();
        assertThat(game.getWinner()).isEqualTo(Winner.CREW);
    }

    @Test
    void 죽은_관전자가_접속_중이어도_생존자가_모두_다음_검사_전에_끊기면_취소한다() {
        flow.begin(game.getGameId());
        game.getPlayer(4L).kill();
        game.getPlayer(5L).kill();
        scheduler.advance(Duration.ofSeconds(3));
        touch(2L, 3L);         // 살아 있는 선장·선원3은 3초, 해적은 0초. 죽은 4·5는 계속 접속 중
        scheduler.advance(Duration.ofSeconds(8));
        touch(4L, 5L);

        flow.checkInactivePlayers(game.getGameId());
        scheduler.runDue();

        assertThat(game.getEndReason()).isEqualTo(GameEndReason.CANCELLED_ALL_DISCONNECTED);
        assertThat(game.getPlayer(1L).isAlive()).isTrue();
    }

    @Test
    void 이미_죽은_사람이_끊기면_방에서만_빼고_사망_안내는_하지_않는다() {
        flow.begin(game.getGameId());
        game.getPlayer(5L).kill();
        passInactiveTimeout(1L, 2L, 3L, 4L);

        flow.checkInactivePlayers(game.getGameId());
        scheduler.runDue();

        assertThat(game.getPlayer(5L).isDeparted()).isTrue();
        assertThat(eventsOf(PlayersDepartedEvent.class)).singleElement()
                .satisfies(e -> assertThat(e.userIds()).containsExactly(5L));
        assertThat(notices()).noneMatch(m -> m.contains("연결이 끊겨"));
        assertThat(game.isEnded()).isFalse();
    }

    @Test
    void 해적이_노린_사람이_밤에_끊기면_공격_선택이_지워지고_해적에게_다시_고르라고_알린다() {
        flow.begin(game.getGameId());
        game.recordNightAction(1L, 5L);
        passInactiveTimeout(1L, 2L, 3L, 4L);

        flow.checkInactivePlayers(game.getGameId());
        scheduler.runDue();

        assertThat(game.getNightActions()).doesNotContainKey(1L);
        assertThat(game.getPhase()).isEqualTo(GamePhase.NIGHT); // 해적이 다시 고를 때까지 밤이 이어진다
        assertThat(eventsOf(PirateNoticeEvent.class))
                .anyMatch(e -> e.message().equals("선원5님이 사라져 공격 대상을 다시 골라야 합니다."));
    }

    @Test
    void 투표한_사람이_끊기면_그_표와_그_사람이_받은_표가_빠지고_아직_안_낸_사람이_있으면_투표가_이어진다() {
        flow.begin(game.getGameId());
        scheduler.advance(Duration.ofSeconds(95)); // 밤 30 + 결과 5 + 낮 60 → 투표
        assertThat(game.getPhase()).isEqualTo(GamePhase.VOTE);
        game.recordVote(2L, 1L);
        game.recordVote(3L, 1L);
        game.recordVote(4L, 5L);
        game.recordVote(5L, 1L);
        passInactiveTimeout(1L, 2L, 3L, 4L);

        flow.checkInactivePlayers(game.getGameId());

        // 예전에는 5의 표가 남아 4표 >= 생존 4명으로 1이 투표하기 전에 판정됐다
        assertThat(game.getPhase()).isEqualTo(GamePhase.VOTE);
        assertThat(game.getVotes()).isEqualTo(Map.of(2L, 1L, 3L, 1L));
    }

    @Test
    void 끊긴_사람만_투표하지_않았으면_타이머를_기다리지_않고_바로_판정한다() {
        flow.begin(game.getGameId());
        scheduler.advance(Duration.ofSeconds(95));
        game.recordVote(1L, 2L);
        game.recordVote(2L, 1L);
        game.recordVote(3L, 1L);
        game.recordVote(4L, 1L);
        passInactiveTimeout(1L, 2L, 3L, 4L);

        flow.checkInactivePlayers(game.getGameId());

        assertThat(game.getLastExecutionResult().executedPlayerId()).isEqualTo(1L);
        assertThat(game.getWinner()).isEqualTo(Winner.CREW);
    }

    @Test
    void 연결_끊김_검사는_설정이_0이면_하지_않는다() {
        GamePhaseProperties off = new GamePhaseProperties(30, 5, 60, 30, 5, 60, 0, 5, 10);
        GameFlowService noCheck = TestGameFlows.create(repository, new NightActionResolver(new Random(0)),
                new VoteResolver(), new WinConditionChecker(), scheduler, off, clock, events::add, tracker);
        noCheck.begin(game.getGameId());
        passInactiveTimeout();

        noCheck.checkInactivePlayers(game.getGameId());

        assertThat(game.isEnded()).isFalse();
        assertThat(game.getPlayers()).allMatch(GamePlayer::isAlive);
    }

    // ---------- 사망자 없는 날 연속 ----------

    @Test
    void 열흘_연속_아무도_죽지_않으면_10일차_투표_결과_직후_취소된다() {
        flow.begin(game.getGameId());

        scheduler.advance(Duration.ofSeconds(DAY_SECONDS * 9 + UNTIL_VOTE_ENDS - 1));
        assertThat(game.getDay()).isEqualTo(10);
        assertThat(game.getPhase()).isEqualTo(GamePhase.VOTE);
        assertThat(game.isEnded()).isFalse();

        scheduler.advance(Duration.ofSeconds(1));
        scheduler.runDue();

        assertThat(game.getEndReason()).isEqualTo(GameEndReason.CANCELLED_NO_DEATHS);
        assertThat(game.getWinner()).isNull();
        assertThat(game.getDay()).isEqualTo(10);
        assertThat(notices()).contains("10일 동안 아무도 죽지 않아 게임이 취소되었습니다. 이번 게임은 전적에 반영되지 않으며, 잠시 후 방이 사라집니다.");
        assertThat(eventsOf(GameEndedEvent.class)).singleElement().satisfies(e -> assertThat(e.cancelled()).isTrue());
    }

    @Test
    void 중간에_누가_죽으면_그날부터_다시_센다() {
        flow.begin(game.getGameId());
        scheduler.advance(Duration.ofSeconds(DAY_SECONDS)); // 2일차 밤
        game.recordNightAction(1L, 5L);                     // 선장이 아직 안 냈으므로 밤은 타이머로 끝난다
        scheduler.advance(Duration.ofSeconds(30));
        assertThat(game.getPlayer(5L).isAlive()).isFalse();

        // 10일차 투표가 끝나도(2일차에 사망자가 있었으므로) 계속된다
        scheduler.advance(Duration.ofSeconds(DAY_SECONDS * 9 - 30));
        assertThat(game.getDay()).isEqualTo(11);
        assertThat(game.isEnded()).isFalse();

        // 12일차 투표 결과 직후 = 2일차 이후 10일 연속 사망자 없음
        scheduler.advance(Duration.ofSeconds(DAY_SECONDS + UNTIL_VOTE_ENDS));
        assertThat(game.getDay()).isEqualTo(12);
        assertThat(game.getEndReason()).isEqualTo(GameEndReason.CANCELLED_NO_DEATHS);
    }

    // ---------- 페이즈 전환 중 예외 ----------

    @Test
    void 타이머로_페이즈를_넘기다_예외가_나면_게임을_취소한다() {
        VoteResolver broken = mock(VoteResolver.class);
        when(broken.resolve(any())).thenThrow(new IllegalStateException("판정 버그"));
        GameFlowService brokenFlow = newFlow(broken);
        brokenFlow.begin(game.getGameId());

        scheduler.advance(Duration.ofSeconds(UNTIL_VOTE_ENDS));
        scheduler.runDue();

        assertThat(game.getEndReason()).isEqualTo(GameEndReason.CANCELLED_ERROR);
        assertThat(game.getWinner()).isNull();
        assertThat(notices()).anyMatch(m -> m.startsWith("서버 오류로 게임이 취소되었습니다."));
        assertThat(eventsOf(GameEndedEvent.class)).singleElement().satisfies(e -> assertThat(e.cancelled()).isTrue());
    }

    @Test
    void 요청_처리_중_판정에서_예외가_나도_게임을_취소하고_예외를_밖으로_던지지_않는다() {
        VoteResolver broken = mock(VoteResolver.class);
        when(broken.resolve(any())).thenThrow(new IllegalStateException("판정 버그"));
        GameFlowService brokenFlow = newFlow(broken);
        brokenFlow.begin(game.getGameId());
        scheduler.advance(Duration.ofSeconds(95));

        assertThatCode(() -> brokenFlow.resolveVote(game)).doesNotThrowAnyException();
        assertThat(game.getEndReason()).isEqualTo(GameEndReason.CANCELLED_ERROR);
    }

    @Test
    void 판정_중_Error가_나도_타이머든_요청이든_게임을_취소한다() {
        VoteResolver broken = mock(VoteResolver.class);
        when(broken.resolve(any())).thenThrow(new StackOverflowError("판정 버그"));
        GameFlowService brokenFlow = newFlow(broken);
        brokenFlow.begin(game.getGameId());
        scheduler.advance(Duration.ofSeconds(95));

        assertThatCode(() -> brokenFlow.resolveVote(game)).doesNotThrowAnyException();
        assertThat(game.getEndReason()).isEqualTo(GameEndReason.CANCELLED_ERROR);

        Game other = newGame();
        brokenFlow.begin(other.getGameId());
        assertThatCode(() -> scheduler.advance(Duration.ofSeconds(UNTIL_VOTE_ENDS))).doesNotThrowAnyException();
        assertThat(other.getEndReason()).isEqualTo(GameEndReason.CANCELLED_ERROR);
    }
}
