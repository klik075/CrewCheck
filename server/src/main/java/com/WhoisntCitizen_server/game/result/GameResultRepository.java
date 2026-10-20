package com.WhoisntCitizen_server.game.result;

import org.springframework.data.domain.Pageable;
import org.springframework.data.jpa.repository.JpaRepository;
import org.springframework.data.jpa.repository.Modifying;
import org.springframework.data.jpa.repository.Query;
import org.springframework.data.repository.query.Param;

import java.time.Instant;
import java.util.List;

/** 게임 결과 저장소. 게임 모듈 밖에서는 GameResultService를 통해서만 쓴다. */
public interface GameResultRepository extends JpaRepository<GameResult, String> {

    /**
     * 아직 전적에 반영하지 않은 결과에만 반영 시각을 적는다. 바뀐 행 수를 돌려준다.
     * 1이면 이 트랜잭션이 반영할 차례를 잡은 것이고, 0이면 이미 반영됐거나 없는 게임이다.
     * 동시에 두 트랜잭션이 부르면 행 잠금 때문에 뒤의 것은 앞의 것이 끝날 때까지 기다렸다가 0을 받는다.
     */
    @Modifying(flushAutomatically = true, clearAutomatically = true)
    @Query("update GameResult r set r.statsAppliedAt = :appliedAt where r.gameId = :gameId and r.statsAppliedAt is null")
    int markStatsApplied(@Param("gameId") String gameId, @Param("appliedAt") Instant appliedAt);

    /** endedBefore 전에 끝났는데 아직 전적에 반영하지 않은 게임 id. 오래된 것부터 */
    @Query("select r.gameId from GameResult r where r.statsAppliedAt is null and r.endedAt < :endedBefore order by r.endedAt")
    List<String> findStatsPendingIds(@Param("endedBefore") Instant endedBefore, Pageable pageable);
}
