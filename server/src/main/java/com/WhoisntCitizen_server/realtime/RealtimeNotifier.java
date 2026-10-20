package com.WhoisntCitizen_server.realtime;

import com.WhoisntCitizen_server.realtime.protocol.ServerMessage;

import java.util.Collection;

/**
 * 다른 모듈(게임·로비·채팅)이 실시간 신호를 보낼 때 쓰는 입구. (4-4~4-6에서 사용)
 *
 * 받는 쪽이 지금 연결돼 있지 않으면 조용히 버린다. 신호는 "다시 가져가라"는 알림일 뿐이고,
 * 클라이언트는 다시 연결될 때 GET으로 상태를 맞추므로 쌓아 두지 않는다.
 *
 * 구현
 *  - LocalRealtimeNotifier (4단계): 이 서버의 연결 목록으로 바로 보낸다. 서버 1대 기준
 *  - RedisRealtimeNotifier (5단계, realtime.redis): Redis Pub/Sub으로 모든 서버에 뿌리고, 각 서버가 자기 연결 목록으로 보낸다
 *  - 고르는 설정: mafia.realtime.notifier (local/redis, 비우면 mafia.server.mode를 따름)
 * 보내는 쪽 코드는 이 인터페이스만 알므로 5단계에서 바뀌지 않는다.
 */
public interface RealtimeNotifier {

    /** 방을 구독 중인 모든 연결에 보낸다 */
    void sendToRoom(long roomId, ServerMessage message);

    /**
     * 방을 구독 중인 연결 중 userIds에 든 사용자의 연결에만 보낸다.
     * 일부에게만 보이는 채팅(사망자·해적·세이렌)처럼 방 안에서도 받을 사람이 정해진 신호에 쓴다.
     */
    void sendToUsersInRoom(long roomId, Collection<Long> userIds, ServerMessage message);

    /**
     * 어떤 방도 구독하지 않은 연결(= 로비에 있는 사람)에 보낸다. excludeUserId의 연결은 뺀다.
     * 새 방이 생겼을 때(room.created) 쓴다.
     */
    void sendToLobby(ServerMessage message, long excludeUserId);

    /** 사용자의 모든 연결에 보낸다 (방 구독과 상관없이) */
    void sendToUser(long userId, ServerMessage message);

    /**
     * 사용자의 연결들을 그 방 구독에서 뺀다. 퇴장·강퇴·연결 끊김으로 방에서 빠졌을 때.
     * 보내기와 같은 인터페이스에 둔 이유: 서버가 여러 대면 그 사용자의 연결이 다른 서버에 있을 수 있어,
     * 5단계에서 보내기와 똑같이 모든 서버에 전달해야 하기 때문이다.
     */
    void removeUserFromRoom(long roomId, long userId);

    /**
     * 사용자의 모든 연결을 서버 쪽에서 닫는다. 같은 계정으로 다른 곳에서 새로 로그인했을 때(closeCode 4409)처럼
     * 이전 연결을 끊어야 할 때 쓴다. 서버가 여러 대면 그 사용자의 연결이 다른 서버에 있을 수 있어, 5단계에서 모든 서버에 전달해야 한다.
     */
    void disconnectUser(long userId, int closeCode, String reason);

    /** 그 방의 구독을 모두 푼다. 방이 삭제됐을 때. (모든 서버에 전달되는 이유는 위와 같다) */
    void removeRoom(long roomId);
}
