package com.sparta.unityaitestproject_server.game.entity;

/**
 * NIGHT -> NIGHT_RESULT -> DAY -> VOTE -> EXECUTION -> NIGHT ... (승리 조건 충족 시 ENDED)
 */
public enum GamePhase {
    NIGHT,          // 3. 밤: 능력 사용
    NIGHT_RESULT,   // 4. 밤 결과 공개
    DAY,            // 5. 낮 토론
    VOTE,           // 6. 투표
    EXECUTION,      // 7. 처형 결과 공개
    ENDED           // 9. 게임 종료
}
