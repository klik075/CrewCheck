package com.sparta.unityaitestproject_server.common.exception;

public class GameNotFoundException extends RuntimeException {
    public GameNotFoundException(String gameId) {
        super("게임을 찾을 수 없습니다: " + gameId);
    }
}
