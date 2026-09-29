package com.sparta.unityaitestproject_server.common.exception;

/** 게임 규칙상 허용되지 않는 요청 (잘못된 페이즈, 죽은 플레이어의 행동 등). */
public class GameRuleException extends RuntimeException {
    public GameRuleException(String message) {
        super(message);
    }
}
