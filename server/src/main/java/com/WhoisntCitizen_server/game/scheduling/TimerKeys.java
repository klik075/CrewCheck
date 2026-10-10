package com.WhoisntCitizen_server.game.scheduling;

/**
 * 타이머 예약 대상의 이름. "같은 대상"인지 가리는 기준이다. (GameTimer 계약 6번)
 *  - 페이즈 타이머: phase:{gameId}:{phaseVersion}  (버전이 다르면 다른 예약)
 *  - 정리 타이머:   cleanup:{gameId}
 * 서버 메모리 구현은 마지막 예약 번호의 키로, Redis 구현(3-2)은 ZSET member로 같은 이름을 쓴다.
 */
public final class TimerKeys {

    static final String PHASE_PREFIX = "phase:";
    static final String CLEANUP_PREFIX = "cleanup:";

    private TimerKeys() {
    }

    public static String phase(String gameId, long phaseVersion) {
        return PHASE_PREFIX + gameId + ":" + phaseVersion;
    }

    public static String cleanup(String gameId) {
        return CLEANUP_PREFIX + gameId;
    }
}
