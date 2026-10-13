package com.WhoisntCitizen_server.game.scheduling;

/**
 * 타이머 예약 대상. TimerKeys가 만든 이름(예: phase:abc:3)을 다시 읽은 결과다.
 * Redis 구현은 ZSET에 이름(문자열)만 저장하므로, 시간이 되면 이름을 읽어 어떤 게임의 무엇인지 되살린다. (3-3)
 *
 * @param phaseVersion 페이즈 타이머의 버전. 정리 타이머면 0 (쓰지 않음)
 */
public record TimerTarget(Kind kind, String gameId, long phaseVersion) {

    public enum Kind {
        /** 페이즈 제한 시간 종료 → GameTimeoutHandler.onPhaseTimeout */
        PHASE,
        /** 끝난 게임의 결과 조회 시간 종료 → GameTimeoutHandler.onCleanup */
        CLEANUP
    }

    public static TimerTarget phase(String gameId, long phaseVersion) {
        return new TimerTarget(Kind.PHASE, gameId, phaseVersion);
    }

    public static TimerTarget cleanup(String gameId) {
        return new TimerTarget(Kind.CLEANUP, gameId, 0);
    }

    /** 이 대상의 이름 (TimerKeys) */
    public String key() {
        return kind == Kind.PHASE ? TimerKeys.phase(gameId, phaseVersion) : TimerKeys.cleanup(gameId);
    }

    /** 시간이 되었을 때 받는 쪽의 알맞은 메서드를 부른다. */
    public void fire(GameTimeoutHandler handler) {
        switch (kind) {
            case PHASE -> handler.onPhaseTimeout(gameId, phaseVersion);
            case CLEANUP -> handler.onCleanup(gameId);
        }
    }
}
