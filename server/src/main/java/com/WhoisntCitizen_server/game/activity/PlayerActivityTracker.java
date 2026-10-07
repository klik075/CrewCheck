package com.WhoisntCitizen_server.game.activity;

import java.time.Instant;
import java.util.Collection;
import java.util.Map;

/**
 * 플레이어별 마지막 요청 시각(접속 기록). 연결 끊김 판정에 쓴다.
 * 게임 상태(Game)와 따로 둔다: 상태 조회(polling)마다 갱신되는 값이라, Game 안에 있으면
 * 게임을 Redis에 둘 때 요청마다 게임 전체를 다시 저장해야 하기 때문이다.
 * 게임 잠금 없이 호출해도 된다. (서버가 잠금 때문에 잠깐 느려진 것만으로 미접속 처리되지 않도록)
 */
public interface PlayerActivityTracker {

    /** 게임 시작 시각으로 모두의 마지막 요청 시각을 맞춘다. (씬을 불러오는 동안 미접속으로 판정되지 않도록) */
    void markAllSeen(String gameId, Collection<Long> playerIds, Instant now);

    /** 플레이어의 요청을 기록한다. 참가자인지는 호출하는 쪽에서 확인한다. */
    void touch(String gameId, Long playerId, Instant now);

    /** 게임의 플레이어별 마지막 요청 시각 (복사본, 없으면 빈 Map) */
    Map<Long, Instant> lastSeen(String gameId);

    /** 끝난 게임의 기록을 지운다. */
    void clear(String gameId);
}
