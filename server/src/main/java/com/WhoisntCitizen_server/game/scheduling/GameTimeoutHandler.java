package com.WhoisntCitizen_server.game.scheduling;

/**
 * {@link GameTimer}에 예약한 시각이 되었을 때 호출되는 쪽. GameFlowService가 구현한다.
 * 타이머 스레드에서 호출되므로 요청한 클라이언트도, 로그인 정보도 없다. 필요한 값은 gameId로 다시 조회한다.
 */
public interface GameTimeoutHandler {

    /** 페이즈 제한 시간 종료. 이미 다음 페이즈로 넘어갔으면(버전 불일치) 무시해야 한다. */
    void onPhaseTimeout(String gameId, long phaseVersion);

    /** 끝난 게임의 결과 조회 시간 종료. 게임을 저장소에서 지운다. */
    void onCleanup(String gameId);
}
