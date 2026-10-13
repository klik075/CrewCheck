package com.WhoisntCitizen_server.common.exception;

/** 403 Forbidden - 현재 채팅할 수 없는 플레이어, 공지 권한 없음 등 */
public class ForbiddenException extends RuntimeException {
    public ForbiddenException(String message) {
        super(message);
    }
}
