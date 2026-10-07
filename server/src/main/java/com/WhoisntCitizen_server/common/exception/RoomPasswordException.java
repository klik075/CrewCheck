package com.WhoisntCitizen_server.common.exception;

/**
 * 비밀방 입장 시 비밀번호가 없거나 틀림 → 403 WRONG_ROOM_PASSWORD.
 *
 * 다른 403(ForbiddenException, code=FORBIDDEN)과 code를 구분하는 이유:
 * Unity가 errorCode만 보고 "비밀번호 팝업을 닫지 않고 다시 입력받기"를 결정할 수 있게 하려고.
 * 틀린 횟수 제한은 없다. (몇 번이든 다시 시도 가능)
 */
public class RoomPasswordException extends RuntimeException {

    public static final String CODE = "WRONG_ROOM_PASSWORD";

    public RoomPasswordException() {
        super("비밀번호가 일치하지 않습니다.");
    }
}
