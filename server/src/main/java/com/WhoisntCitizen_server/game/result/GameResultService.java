package com.WhoisntCitizen_server.game.result;

import com.WhoisntCitizen_server.game.event.GameEndedEvent;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;
import org.springframework.dao.DataIntegrityViolationException;
import org.springframework.data.domain.PageRequest;
import org.springframework.stereotype.Service;
import org.springframework.transaction.annotation.Propagation;
import org.springframework.transaction.annotation.Transactional;

import java.time.Clock;
import java.time.Instant;
import java.util.List;
import java.util.Optional;

/**
 * 게임 결과(game_results) 기록과 전적 반영 선점.
 *
 *   게임 종료 ─ record ─▶ game_results (stats_applied_at = NULL)
 *                              │
 *     전적 리스너(바로) / PendingGameStatsRelay(나중에) ─ claimForStats ─▶ stats_applied_at 기록 + 전적 증가 (한 트랜잭션)
 *
 * 같은 게임을 몇 번 반영하려 해도 claimForStats는 한 번만 결과를 돌려주므로 전적은 정확히 한 번 오른다.
 * 게임 모듈은 회원 코드를 모른다. 전적을 올리는 쪽(UserStatsService)이 이 서비스를 부른다.
 */
@Service
public class GameResultService implements GameResultRecorder {

    private static final Logger log = LoggerFactory.getLogger(GameResultService.class);

    private final GameResultRepository repository;
    private final Clock clock;

    public GameResultService(GameResultRepository repository, Clock clock) {
        this.repository = repository;
        this.clock = clock;
    }

    @Override
    public void record(GameEndedEvent event) {
        if (!event.countsForStats() || repository.existsById(event.gameId())) {
            return;
        }
        try {
            repository.saveAndFlush(GameResult.of(event, clock.instant()));
            log.info("[{}] 게임 결과 기록 (승리: {})", event.gameId(), event.winner());
        } catch (DataIntegrityViolationException e) {
            // 확인한 뒤 넣기 전에 다른 쪽이 같은 게임을 먼저 넣었다면 이미 남은 것이다. 다른 제약 위반이면 그대로 던진다
            if (!repository.existsById(event.gameId())) {
                throw e;
            }
            log.debug("[{}] 게임 결과가 이미 기록돼 있음", event.gameId());
        }
    }

    /**
     * 전적 반영 선점. 이 호출이 반영할 차례를 잡았으면 결과를 돌려주고, 이미 반영됐거나 기록이 없는 게임이면 빈 값.
     * 호출한 쪽의 트랜잭션 안에서만 부를 수 있다(MANDATORY). 선점(반영 시각 기록)과 전적 증가가 함께 커밋되거나
     * 함께 롤백돼야 "반영 시각만 남고 전적은 안 오른" 상태나 "두 번 오른" 상태가 생기지 않는다.
     */
    @Transactional(propagation = Propagation.MANDATORY)
    public Optional<GameResult> claimForStats(String gameId) {
        if (repository.markStatsApplied(gameId, clock.instant()) == 0) {
            return Optional.empty();
        }
        return repository.findById(gameId);
    }

    /** endedBefore 전에 끝났는데 아직 전적에 반영하지 않은 게임 id를 오래된 것부터 최대 limit개 */
    @Transactional(readOnly = true)
    public List<String> findStatsPendingIds(Instant endedBefore, int limit) {
        return repository.findStatsPendingIds(endedBefore, PageRequest.of(0, limit));
    }
}
