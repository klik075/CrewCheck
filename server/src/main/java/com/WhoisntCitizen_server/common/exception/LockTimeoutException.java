package com.WhoisntCitizen_server.common.exception;

import java.time.Duration;

/**
 * 잠금(게임 잠금·방 잠금)을 정해진 시간 안에 잡지 못했을 때 던진다.
 *
 * 예전 잠금은 무한정 기다렸다. 서버가 여러 대가 되면 한 서버가 잠금을 쥔 채 멈췄을 때
 * 다른 서버들이 줄줄이 멈출 수 있어서, 모든 잠금 구현은 대기 시간을 넘기면 이 예외를 던진다.
 * 잠금을 잡지 못했으므로 작업(action)은 실행되지 않은 상태다. (상태가 바뀌지 않았으니 다시 시도해도 안전하다)
 *
 * 처리
 *  - API 요청: GlobalExceptionHandler가 503 + Retry-After: 1 (code: LOCK_BUSY)로 바꾼다.
 *  - 페이즈 타이머·종료 게임 정리: GameFlowService가 1초 뒤 다시 시도한다. (그냥 끝나면 게임이 그 페이즈에 멈춘다)
 *  - 서버 시작 시 타이머 복구: GameTimerRecovery가 몇 번 더 시도한다.
 *  - 연결 끊김 검사: 이번 검사만 건너뛰고 다음 검사에서 다시 확인한다.
 */
public class LockTimeoutException extends RuntimeException {

    private final String lockName;
    private final Object key;
    private final Duration waited;

    public LockTimeoutException(String lockName, Object key, Duration waited) {
        super(lockName + " 잠금을 " + waited.toMillis() + "ms 안에 잡지 못했습니다. (key=" + key + ")");
        this.lockName = lockName;
        this.key = key;
        this.waited = waited;
    }

    /** 어떤 잠금인지 ("게임", "방") */
    public String getLockName() {
        return lockName;
    }

    /** 잠그려던 대상 (gameId, roomId) */
    public Object getKey() {
        return key;
    }

    /** 기다린 시간 */
    public Duration getWaited() {
        return waited;
    }
}
