package com.WhoisntCitizen_server.game.service;

import com.WhoisntCitizen_server.common.config.GamePhaseProperties;
import com.WhoisntCitizen_server.game.activity.LocalPlayerActivityTracker;
import com.WhoisntCitizen_server.game.entity.Game;
import com.WhoisntCitizen_server.game.entity.GamePlayer;
import com.WhoisntCitizen_server.game.entity.Winner;
import com.WhoisntCitizen_server.game.event.GameEndedEvent;
import com.WhoisntCitizen_server.game.lock.LocalGameLock;
import com.WhoisntCitizen_server.game.repository.InMemoryGameRepository;
import com.WhoisntCitizen_server.game.result.GameResultRecorder;
import com.WhoisntCitizen_server.night.service.NightActionResolver;
import com.WhoisntCitizen_server.support.ManualTaskScheduler;
import com.WhoisntCitizen_server.support.MutableClock;
import com.WhoisntCitizen_server.support.TestGameFlows;
import com.WhoisntCitizen_server.support.TestRoles;
import com.WhoisntCitizen_server.vote.service.VoteResolver;
import com.WhoisntCitizen_server.vote.service.VoteService;
import org.junit.jupiter.api.Test;

import java.time.Duration;
import java.time.Instant;
import java.util.ArrayList;
import java.util.List;
import java.util.Random;

import static org.assertj.core.api.Assertions.assertThat;

/**
 * 게임 결과 기록(outbox): 승패가 나면 게임 상태(ENDED)를 저장하기 전에 결과를 DB에 남긴다.
 * 그래야 저장 직후 서버가 꺼져 종료 이벤트(서버 메모리)가 사라져도 결과는 남아 전적을 나중에 반영할 수 있다.
 */
class GameResultRecordingTest {

    private static final Instant NOW = Instant.parse("2026-10-18T12:00:00Z");
    // 밤 30, 결과 5, 낮 60, 투표 30, 처형 5. 종료 후 60초 보관, 연결 끊김 검사 끔, 10일 무사망 시 취소
    private static final GamePhaseProperties PROPS = new GamePhaseProperties(30, 5, 60, 30, 5, 60, 0, 5, 10, 10);
    private static final int UNTIL_VOTE = 95; // 밤 30 + 결과 5 + 낮 60

    private final MutableClock clock = new MutableClock(NOW);
    private final ManualTaskScheduler scheduler = new ManualTaskScheduler(clock);
    private final List<String> log = new ArrayList<>();
    private final List<Object> events = new ArrayList<>();

    /** 저장할 때 그 순간의 페이즈를 log에 남기는 저장소 */
    private final InMemoryGameRepository repository = new InMemoryGameRepository() {
        @Override
        public Game save(Game game) {
            log.add("save:" + game.getPhase());
            return super.save(game);
        }
    };

    /** 1: 해적, 2: 선장, 3~5: 선원으로 시작해 첫 투표에서 해적을 처형한다 → 선원 승리 */
    private String playUntilCrewWins(GameResultRecorder recorder) {
        GameFlowService flow = TestGameFlows.create(repository, new NightActionResolver(new Random(0)), new VoteResolver(),
                new WinConditionChecker(), scheduler, PROPS, clock, events::add, new LocalPlayerActivityTracker(), recorder);
        VoteService voteService = new VoteService(repository, flow, new LocalGameLock());
        String gameId = repository.save(new Game("1", List.of(
                new GamePlayer(1L, "해적", TestRoles.RAIDER),
                new GamePlayer(2L, "선장", TestRoles.CAPTAIN),
                new GamePlayer(3L, "선원3", TestRoles.SAILOR),
                new GamePlayer(4L, "선원4", TestRoles.SAILOR),
                new GamePlayer(5L, "선원5", TestRoles.SAILOR)), true)).getGameId();
        flow.begin(gameId);
        scheduler.advance(Duration.ofSeconds(UNTIL_VOTE));
        scheduler.runDue();

        voteService.vote(gameId, 1L, 2L, true);
        voteService.vote(gameId, 2L, 1L, true);
        voteService.vote(gameId, 3L, 1L, true);
        voteService.vote(gameId, 4L, 1L, true);
        voteService.vote(gameId, 5L, 1L, true);
        scheduler.runDue(); // 잠금이 풀린 뒤 발행되는 종료 이벤트
        return gameId;
    }

    @Test
    void 승패가_나면_게임_상태를_저장하기_전에_결과를_기록한다() {
        List<GameEndedEvent> recorded = new ArrayList<>();
        String gameId = playUntilCrewWins(event -> {
            recorded.add(event);
            log.add("record");
        });

        assertThat(recorded).singleElement().satisfies(event -> {
            assertThat(event.gameId()).isEqualTo(gameId);
            assertThat(event.winner()).isEqualTo(Winner.CREW);
            assertThat(event.countsForStats()).isTrue();
        });
        assertThat(log).containsOnlyOnce("record", "save:ENDED").containsSubsequence("record", "save:ENDED");
    }

    @Test
    void 결과_기록이_실패해도_게임은_끝나고_종료_이벤트가_발행된다() {
        String gameId = playUntilCrewWins(event -> {
            throw new IllegalStateException("DB 연결 실패");
        });

        Game game = repository.findById(gameId).orElseThrow();
        assertThat(game.isEnded()).isTrue();
        assertThat(game.getWinner()).isEqualTo(Winner.CREW);
        // 전적 리스너가 이 이벤트로 결과를 다시 남긴다 (UserStatsListener)
        assertThat(events).filteredOn(GameEndedEvent.class::isInstance).singleElement()
                .satisfies(e -> assertThat(((GameEndedEvent) e).gameId()).isEqualTo(gameId));
    }
}
