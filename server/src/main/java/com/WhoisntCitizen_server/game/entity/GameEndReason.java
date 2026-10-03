package com.WhoisntCitizen_server.game.entity;

/**
 * 게임이 끝난 이유. 진행 중이면 null.
 * 취소(CANCELLED_*)된 게임은 승리 팀이 없고(winner = null) 전적에 반영하지 않으며,
 * 결과 조회 시간(endedRetentionSeconds)이 지나면 방도 함께 삭제된다.
 */
public enum GameEndReason {
    WIN,                        // 승리 팀이 정해져 정상 종료
    CANCELLED_ALL_DISCONNECTED, // 살아 있는 플레이어가 모두 연결이 끊김
    CANCELLED_NO_DEATHS,        // 정해진 일수 동안 연속으로 아무도 죽지 않음
    CANCELLED_ERROR;            // 페이즈 전환 중 서버 오류

    public boolean isCancelled() {
        return this != WIN;
    }
}
