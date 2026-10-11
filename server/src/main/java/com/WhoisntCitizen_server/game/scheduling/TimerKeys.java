package com.WhoisntCitizen_server.game.scheduling;

/**
 * 타이머 예약 대상의 이름. "같은 대상"인지 가리는 기준이다. (GameTimer 계약 6번)
 *  - 페이즈 타이머: phase:{gameId}:{phaseVersion}  (버전이 다르면 다른 예약)
 *  - 정리 타이머:   cleanup:{gameId}
 * 서버 메모리 구현은 마지막 예약 번호의 키로, Redis 구현은 ZSET member로 같은 이름을 쓴다.
 * Redis에서 꺼낸 이름은 parse()로 다시 대상(TimerTarget)으로 바꾼다.
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

    /**
     * 이름을 대상으로 되돌린다. 버전은 마지막 ':' 뒤에서 읽으므로 gameId에 ':'가 있어도 된다.
     * @throws IllegalArgumentException 형식이 맞지 않을 때 (다른 프로그램이 넣은 값, 예전 형식 등)
     */
    public static TimerTarget parse(String key) {
        if (key == null) {
            throw new IllegalArgumentException("타이머 이름이 없습니다");
        }
        if (key.startsWith(CLEANUP_PREFIX)) {
            String gameId = key.substring(CLEANUP_PREFIX.length());
            if (gameId.isEmpty()) {
                throw new IllegalArgumentException("타이머 이름에 gameId가 없습니다: " + key);
            }
            return TimerTarget.cleanup(gameId);
        }
        if (key.startsWith(PHASE_PREFIX)) {
            String rest = key.substring(PHASE_PREFIX.length());
            int colon = rest.lastIndexOf(':');
            if (colon <= 0 || colon == rest.length() - 1) {
                throw new IllegalArgumentException("페이즈 타이머 이름 형식이 아닙니다: " + key);
            }
            try {
                return TimerTarget.phase(rest.substring(0, colon), Long.parseLong(rest.substring(colon + 1)));
            } catch (NumberFormatException e) {
                throw new IllegalArgumentException("페이즈 타이머 버전이 숫자가 아닙니다: " + key, e);
            }
        }
        throw new IllegalArgumentException("알 수 없는 타이머 이름입니다: " + key);
    }
}
