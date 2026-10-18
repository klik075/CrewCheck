package com.WhoisntCitizen_server.game.result;

import com.WhoisntCitizen_server.game.event.GameEndedEvent;

/**
 * 게임이 끝날 때 결과를 DB(game_results)에 남긴다. 구현은 GameResultService.
 * GameFlowService가 게임 잠금 안에서, 게임 상태(ENDED)를 저장하기 전에 부른다.
 * 그래서 게임 종료 직후 서버가 꺼져 GameEndedEvent(서버 메모리)가 사라져도 결과는 DB에 남고,
 * 전적은 PendingGameStatsRelay가 나중에 반영한다. (outbox)
 */
public interface GameResultRecorder {

    /** 결과를 기록하지 않는다. 결과 DB 없이 게임 흐름만 확인하는 테스트용 */
    GameResultRecorder NONE = event -> { };

    /**
     * 전적에 들어가는 게임(GameEndedEvent.countsForStats)이면 결과를 남긴다. 그 밖의 게임은 무시한다.
     * 같은 gameId가 이미 있으면 그대로 둔다. 여러 번 불러도 결과는 한 번만 남는다.
     * DB 오류는 그대로 던진다. 실패를 어떻게 다룰지는 부른 쪽이 정한다.
     */
    void record(GameEndedEvent event);
}
