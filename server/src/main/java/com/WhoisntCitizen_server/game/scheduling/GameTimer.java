package com.WhoisntCitizen_server.game.scheduling;

import java.time.Instant;

/**
 * 게임 타이머 예약.
 * 실행할 코드(Runnable)가 아니라 "어떤 게임의 무엇을 언제"만 넘긴다.
 * 그래야 서버 메모리 대신 Redis 같은 외부 저장소에 예약을 보관하는 구현으로 바꿀 수 있다.
 * 시간이 되면 구현체가 {@link GameTimeoutHandler}의 메서드를 호출한다.
 */
public interface GameTimer {

    /** at에 phaseVersion 페이즈의 제한 시간 종료를 알린다. 그사이 페이즈가 바뀌었으면 받는 쪽이 버전으로 무시한다. */
    void schedulePhaseTimeout(String gameId, long phaseVersion, Instant at);

    /** at에 끝난 게임의 정리(결과 조회 시간 종료)를 알린다. */
    void scheduleCleanup(String gameId, Instant at);
}
