package com.WhoisntCitizen_server.user.service;

import com.WhoisntCitizen_server.game.entity.GameEndReason;
import com.WhoisntCitizen_server.game.entity.Winner;
import com.WhoisntCitizen_server.game.event.GameEndedEvent;
import com.WhoisntCitizen_server.game.result.GameResultService;
import com.WhoisntCitizen_server.jobs.domain.Faction;
import com.WhoisntCitizen_server.user.entity.User;
import com.WhoisntCitizen_server.user.repository.UserRepository;
import org.junit.jupiter.api.AfterEach;
import org.junit.jupiter.api.BeforeEach;
import org.junit.jupiter.api.Test;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.boot.test.context.SpringBootTest;
import org.springframework.jdbc.core.JdbcTemplate;
import org.springframework.transaction.IllegalTransactionStateException;
import org.springframework.transaction.PlatformTransactionManager;
import org.springframework.transaction.support.TransactionTemplate;

import java.lang.reflect.Proxy;
import java.sql.Timestamp;
import java.time.Instant;
import java.util.ArrayList;
import java.util.List;
import java.util.OptionalInt;
import java.util.UUID;
import java.util.concurrent.Callable;
import java.util.concurrent.CountDownLatch;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;
import java.util.concurrent.Future;

import static org.assertj.core.api.Assertions.assertThat;
import static org.assertj.core.api.Assertions.assertThatThrownBy;

/**
 * 전적 반영 outbox: 게임 결과(game_results)를 기준으로 같은 게임은 정확히 한 번만 전적에 반영한다.
 * 실제 Spring 설정 + H2(Flyway 마이그레이션)로 확인한다.
 */
@SpringBootTest
class GameStatsOutboxTest {

    // 다른 테스트와 겹치지 않는 회원 (테스트마다 넣고 지운다)
    private static final long PIRATE_USER = 9101L;
    private static final long CREW_USER = 9102L;

    @Autowired GameResultService gameResultService;
    @Autowired UserStatsService userStatsService;
    @Autowired UserStatsListener userStatsListener;
    @Autowired PendingGameStatsRelay relay;
    @Autowired UserRepository userRepository;
    @Autowired JdbcTemplate jdbc;
    @Autowired PlatformTransactionManager transactionManager;

    private final String gameId = "outbox-" + UUID.randomUUID();

    @BeforeEach
    void setUp() {
        cleanUp();
        // 같은 H2를 쓰는 다른 테스트가 남긴 결과가 재시도 대상에 섞이지 않게 반영된 것으로 둔다
        jdbc.update("UPDATE game_results SET stats_applied_at = CURRENT_TIMESTAMP WHERE stats_applied_at IS NULL");
        insertUser(PIRATE_USER, "아웃박스해적");
        insertUser(CREW_USER, "아웃박스선원");
    }

    @AfterEach
    void cleanUp() {
        jdbc.update("DELETE FROM game_result_players WHERE game_id LIKE 'outbox-%'");
        jdbc.update("DELETE FROM game_results WHERE game_id LIKE 'outbox-%'");
        jdbc.update("DELETE FROM users WHERE id IN (?, ?)", PIRATE_USER, CREW_USER);
        jdbc.update("DELETE FROM members WHERE id IN (?, ?)", PIRATE_USER, CREW_USER);
    }

    private void insertUser(long id, String nickname) {
        jdbc.update("INSERT INTO members (id, username, provider) VALUES (?, ?, 'LOCAL')", id, "outbox-" + id);
        jdbc.update("INSERT INTO users (id, member_id, nickname) VALUES (?, ?, ?)", id, id, nickname);
    }

    /** 해적(PIRATE_USER)이 처형돼 선원(CREW_USER)이 이긴 게임 */
    private GameEndedEvent crewWin(String gameId) {
        return new GameEndedEvent(gameId, "1", Winner.CREW, GameEndReason.WIN, 2, true, List.of(
                new GameEndedEvent.PlayerOutcome(PIRATE_USER, "PIRATE_RAIDER", Faction.PIRATE, false, false),
                new GameEndedEvent.PlayerOutcome(CREW_USER, "CREW_SAILOR", Faction.CREW, true, true)));
    }

    private User user(long id) {
        return userRepository.findById(id).orElseThrow();
    }

    private Timestamp statsAppliedAt(String gameId) {
        return jdbc.queryForObject("SELECT stats_applied_at FROM game_results WHERE game_id = ?", Timestamp.class, gameId);
    }

    /** 재시도 대상이 되도록 결과의 종료 시각을 1분 전으로 옮긴다 (막 끝난 게임은 PENDING_GRACE 동안 건너뛴다) */
    private void endedOneMinuteAgo(String gameId) {
        jdbc.update("UPDATE game_results SET ended_at = ? WHERE game_id = ?",
                Timestamp.from(Instant.now().minusSeconds(60)), gameId);
    }

    @Test
    void 같은_게임을_여러_번_반영해도_전적은_한_번만_오른다() {
        gameResultService.record(crewWin(gameId));
        gameResultService.record(crewWin(gameId)); // 이미 있으면 그대로 둔다

        assertThat(userStatsService.applyGameResult(gameId)).hasValue(2);
        assertThat(userStatsService.applyGameResult(gameId)).isEmpty();

        assertThat(user(CREW_USER)).satisfies(u -> {
            assertThat(u.getPlayCount()).isEqualTo(1);
            assertThat(u.getWinCount()).isEqualTo(1);
            assertThat(u.getCrewPlayCount()).isEqualTo(1);
            assertThat(u.getCrewWinCount()).isEqualTo(1);
        });
        assertThat(user(PIRATE_USER)).satisfies(u -> {
            assertThat(u.getPlayCount()).isEqualTo(1);
            assertThat(u.getWinCount()).isZero();
            assertThat(u.getPiratePlayCount()).isEqualTo(1);
        });
        assertThat(jdbc.queryForObject("SELECT COUNT(*) FROM game_result_players WHERE game_id = ?", Integer.class, gameId))
                .isEqualTo(2);
        assertThat(statsAppliedAt(gameId)).isNotNull();
    }

    @Test
    void 종료_이벤트를_받기_전에_서버가_꺼진_게임은_재시도가_반영한다() {
        // 게임이 끝날 때 결과만 남고, 종료 이벤트(서버 메모리)는 사라졌다
        gameResultService.record(crewWin(gameId));
        assertThat(relay.relayPending()).isZero(); // 막 끝난 게임은 리스너 몫이라 건너뛴다

        endedOneMinuteAgo(gameId);
        assertThat(relay.relayPending()).isEqualTo(1);
        assertThat(relay.relayPending()).isZero();  // 이미 반영됨

        assertThat(user(CREW_USER).getWinCount()).isEqualTo(1);
        assertThat(user(PIRATE_USER).getPlayCount()).isEqualTo(1);
    }

    @Test
    void 종료_이벤트가_두_번_와도_전적은_한_번만_오르고_재시도도_다시_하지_않는다() {
        // 게임 종료 때 결과 기록이 DB 오류로 실패했다고 보고, 리스너가 결과를 남기며 반영한다
        userStatsListener.onGameEnded(crewWin(gameId));
        userStatsListener.onGameEnded(crewWin(gameId));
        endedOneMinuteAgo(gameId);

        assertThat(relay.relayPending()).isZero();
        assertThat(user(CREW_USER).getPlayCount()).isEqualTo(1);
        assertThat(user(PIRATE_USER).getPlayCount()).isEqualTo(1);
    }

    @Test
    void 전적을_올리다_실패하면_반영_표시도_롤백돼_나중에_다시_반영된다() {
        gameResultService.record(crewWin(gameId));
        UserRepository broken = (UserRepository) Proxy.newProxyInstance(UserRepository.class.getClassLoader(),
                new Class<?>[]{UserRepository.class}, (proxy, method, args) -> {
                    throw new IllegalStateException("DB 오류");
                });
        UserStatsService failing = new UserStatsService(broken, gameResultService);
        TransactionTemplate tx = new TransactionTemplate(transactionManager);

        assertThatThrownBy(() -> tx.executeWithoutResult(status -> failing.applyGameResult(gameId)))
                .hasMessage("DB 오류");
        assertThat(statsAppliedAt(gameId)).isNull();

        endedOneMinuteAgo(gameId);
        assertThat(relay.relayPending()).isEqualTo(1);
        assertThat(user(CREW_USER).getPlayCount()).isEqualTo(1);
    }

    @Test
    void 여러_곳에서_동시에_반영해도_전적은_한_번만_오른다() throws Exception {
        gameResultService.record(crewWin(gameId));
        int threads = 8;
        CountDownLatch start = new CountDownLatch(1);
        ExecutorService pool = Executors.newFixedThreadPool(threads);
        try {
            List<Future<OptionalInt>> results = new ArrayList<>();
            for (int i = 0; i < threads; i++) {
                Callable<OptionalInt> apply = () -> {
                    start.await();
                    try {
                        return userStatsService.applyGameResult(gameId);
                    } catch (RuntimeException e) {
                        return OptionalInt.empty(); // 잠금 충돌로 실패한 쪽은 반영하지 않은 것 (롤백)
                    }
                };
                results.add(pool.submit(apply));
            }
            start.countDown();

            int applied = 0;
            for (Future<OptionalInt> result : results) {
                if (result.get().isPresent()) {
                    applied++;
                }
            }
            assertThat(applied).isEqualTo(1);
        } finally {
            pool.shutdownNow();
        }
        assertThat(user(CREW_USER).getPlayCount()).isEqualTo(1);
        assertThat(user(CREW_USER).getWinCount()).isEqualTo(1);
    }

    @Test
    void 취소된_게임과_개발용_게임은_결과를_남기지_않는다() {
        String cancelledId = gameId + "-cancelled";
        String devId = gameId + "-dev";
        gameResultService.record(new GameEndedEvent(cancelledId, "1", null, GameEndReason.CANCELLED_NO_DEATHS, 10, true,
                crewWin(cancelledId).outcomes()));
        gameResultService.record(new GameEndedEvent(devId, "dev", Winner.CREW, GameEndReason.WIN, 2, false,
                crewWin(devId).outcomes()));

        assertThat(jdbc.queryForObject("SELECT COUNT(*) FROM game_results WHERE game_id IN (?, ?)", Integer.class,
                cancelledId, devId)).isZero();
    }

    @Test
    void 반영_선점은_전적을_올리는_트랜잭션_안에서만_부를_수_있다() {
        gameResultService.record(crewWin(gameId));

        assertThatThrownBy(() -> gameResultService.claimForStats(gameId))
                .isInstanceOf(IllegalTransactionStateException.class);
        assertThat(statsAppliedAt(gameId)).isNull();
    }
}
