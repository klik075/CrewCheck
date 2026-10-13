package com.WhoisntCitizen_server.game.activity;

import java.time.Instant;
import java.util.Collection;
import java.util.Map;

/**
 * 플레이어별 마지막 요청 시각(접속 기록). 연결 끊김 판정에 쓴다.
 * 게임 상태(Game)와 따로 둔다: 상태 조회(polling)마다 갱신되는 값이라, Game 안에 있으면
 * 게임을 Redis에 둘 때 요청마다 게임 전체를 다시 저장해야 하기 때문이다.
 * 게임 잠금 없이 호출해도 된다. (서버가 잠금 때문에 잠깐 느려진 것만으로 미접속 처리되지 않도록)
 *
 * 구현이 지켜야 할 것 (PlayerActivityTrackerContractTest)
 *  1. 기록한 시각을 그대로 돌려준다
 *  2. 시작할 때 참가자 모두를 같은 시각으로 기록한다
 *  3. 더 늦은 시각만 반영한다. 이미 기록된 시각보다 이른 시각은 무시한다
 *     잠금 없이 여러 요청(서버 여러 대)이 동시에 기록하므로, 늦게 도착한 오래된 요청이
 *     최신 기록을 덮어써 멀쩡한 플레이어가 미접속으로 판정되지 않게 한다
 *  4. 게임끼리 기록이 섞이지 않는다
 *  5. 지우면 그 게임의 기록만 사라진다
 *  6. 조회 결과는 복사본이다. 이후 기록에 바뀌지 않고, 결과를 고쳐도 기록에 영향이 없다
 *  7. gameId나 playerId가 없으면(null) 기록하지 않는다
 */
public interface PlayerActivityTracker {

    /** 게임 시작 시각으로 모두의 마지막 요청 시각을 맞춘다. (씬을 불러오는 동안 미접속으로 판정되지 않도록) 더 늦은 기록이 있으면 그대로 둔다. */
    void markAllSeen(String gameId, Collection<Long> playerIds, Instant now);

    /** 플레이어의 요청을 기록한다. 기존 기록보다 늦을 때만 바꾼다. 참가자인지는 호출하는 쪽에서 확인한다. */
    void touch(String gameId, Long playerId, Instant now);

    /** 게임의 플레이어별 마지막 요청 시각 (복사본, 없으면 빈 Map) */
    Map<Long, Instant> lastSeen(String gameId);

    /** 끝난 게임의 기록을 지운다. */
    void clear(String gameId);
}
