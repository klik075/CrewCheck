package com.WhoisntCitizen_server.realtime.redis;

import com.fasterxml.jackson.annotation.JsonIgnoreProperties;
import com.fasterxml.jackson.annotation.JsonInclude;

import java.util.List;

/**
 * 서버끼리 Redis 채널로 주고받는 실시간 신호 한 건. (5단계)
 *
 * RealtimeNotifier의 메서드 호출 하나가 신호 하나가 된다. op가 어떤 메서드인지, 나머지 칸이 그 메서드의 인자다.
 * 받은 서버는 같은 메서드를 자기 LocalRealtimeNotifier에 그대로 다시 부른다. (RedisRealtimeNotifier.deliver)
 *
 *   {"v":1,"origin":"host-1a2b3c4d","op":"ROOM","roomId":3,"message":"{\"type\":\"room.changed\",\"roomId\":3}"}
 *
 *  - v: 신호 형식 버전. 형식을 바꾸면 올린다. (서버를 한 대씩 교체하는 동안 옛 서버와 새 서버가 섞여 있을 수 있다)
 *  - origin: 보낸 서버의 ID. 보낸 서버는 자기 연결에 이미 직접 보냈으므로, 채널로 되돌아온 자기 신호는 버린다
 *  - message: 클라이언트에게 보낼 ServerMessage를 JSON 문자열로 바꾼 것. 보내기 신호(ROOM·USERS_IN_ROOM·USER)에만 있다
 *
 * 비어 있는 칸은 JSON에 넣지 않는다(NON_NULL). 모르는 칸은 무시한다. (새 서버가 칸을 추가해도 옛 서버가 깨지지 않게)
 */
@JsonInclude(JsonInclude.Include.NON_NULL)
@JsonIgnoreProperties(ignoreUnknown = true)
public record RealtimeSignal(
        int v,
        String origin,
        Op op,
        Long roomId,
        Long userId,
        List<Long> userIds,
        Integer closeCode,
        String reason,
        String message
) {

    public static final int VERSION = 1;

    /** RealtimeNotifier의 메서드와 1:1로 대응한다 */
    public enum Op {
        /** sendToRoom */
        ROOM,
        /** sendToUsersInRoom */
        USERS_IN_ROOM,
        /** sendToUser */
        USER,
        /** disconnectUser */
        DISCONNECT_USER,
        /** removeUserFromRoom */
        REMOVE_USER_FROM_ROOM,
        /** removeRoom */
        REMOVE_ROOM
    }

    public static RealtimeSignal room(String origin, long roomId, String message) {
        return new RealtimeSignal(VERSION, origin, Op.ROOM, roomId, null, null, null, null, message);
    }

    public static RealtimeSignal usersInRoom(String origin, long roomId, List<Long> userIds, String message) {
        return new RealtimeSignal(VERSION, origin, Op.USERS_IN_ROOM, roomId, null, List.copyOf(userIds), null, null, message);
    }

    public static RealtimeSignal user(String origin, long userId, String message) {
        return new RealtimeSignal(VERSION, origin, Op.USER, null, userId, null, null, null, message);
    }

    public static RealtimeSignal disconnectUser(String origin, long userId, int closeCode, String reason) {
        return new RealtimeSignal(VERSION, origin, Op.DISCONNECT_USER, null, userId, null, closeCode, reason, null);
    }

    public static RealtimeSignal removeUserFromRoom(String origin, long roomId, long userId) {
        return new RealtimeSignal(VERSION, origin, Op.REMOVE_USER_FROM_ROOM, roomId, userId, null, null, null, null);
    }

    public static RealtimeSignal removeRoom(String origin, long roomId) {
        return new RealtimeSignal(VERSION, origin, Op.REMOVE_ROOM, roomId, null, null, null, null, null);
    }
}
