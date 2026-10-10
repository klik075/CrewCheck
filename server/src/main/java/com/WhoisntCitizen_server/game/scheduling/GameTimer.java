package com.WhoisntCitizen_server.game.scheduling;

import java.time.Instant;

/**
 * 게임 타이머 예약.
 * 실행할 코드(Runnable)가 아니라 "어떤 게임의 무엇을 언제"만 넘긴다.
 * 그래야 서버 메모리 대신 Redis 같은 외부 저장소에 예약을 보관하는 구현으로 바꿀 수 있다.
 * 시간이 되면 구현체가 {@link GameTimeoutHandler}의 메서드를 호출한다.
 *
 * 구현이 지켜야 할 것 (테스트: GameTimerContractTest)
 *  - 예약 시각 전에는 실행하지 않고, 시각이 되면 예약할 때 준 값(gameId, phaseVersion)으로 실행한다
 *  - 이미 지난 시각으로 예약하면 바로 실행한다
 *  - 같은 대상(같은 게임의 같은 버전 / 같은 게임의 정리)을 다시 예약하면 마지막 예약 하나만 남는다 (TimerKeys)
 *  - 실행 중에 같은 대상을 다시 예약하면 그 새 예약은 남는다 (잠금 실패 후 재시도)
 *  - 실행이 예외로 끝나도 밖으로 던지지 않고 다른 예약은 계속 실행한다
 * 받는 쪽은 버전으로 지난 페이즈를 무시하므로, 같은 예약이 드물게 두 번 실행되는 것은 허용한다.
 */
public interface GameTimer {

    /** at에 phaseVersion 페이즈의 제한 시간 종료를 알린다. 그사이 페이즈가 바뀌었으면 받는 쪽이 버전으로 무시한다. */
    void schedulePhaseTimeout(String gameId, long phaseVersion, Instant at);

    /** at에 끝난 게임의 정리(결과 조회 시간 종료)를 알린다. */
    void scheduleCleanup(String gameId, Instant at);
}
