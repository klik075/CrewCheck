package com.WhoisntCitizen_server.common.exception;

/**
 * 방장에게 추방된 방에 다시 들어오려 함 → 403 KICKED_FROM_ROOM.
 *
 * 다른 403(FORBIDDEN, WRONG_ROOM_PASSWORD)과 code를 구분해 Unity가 비밀번호 팝업을 다시 띄우지 않게 한다.
 * 추방 기록은 방이 사라질 때까지 남는다. (Room.kickedUserIds)
 */
public class RoomKickedException extends RuntimeException {

    public static final String CODE = "KICKED_FROM_ROOM";

    public RoomKickedException() {
        super("방장에 의해 추방된 방에는 다시 들어갈 수 없습니다.");
    }
}
