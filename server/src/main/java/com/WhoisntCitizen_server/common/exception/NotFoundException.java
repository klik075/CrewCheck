package com.WhoisntCitizen_server.common.exception;

/** 404 Not Found - 채팅방 또는 유저가 존재하지 않음 등 */
public class NotFoundException extends RuntimeException {
    public NotFoundException(String message) {
        super(message);
    }
}
