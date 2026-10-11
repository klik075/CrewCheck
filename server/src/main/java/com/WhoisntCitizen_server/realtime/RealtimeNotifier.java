package com.WhoisntCitizen_server.realtime;

import com.WhoisntCitizen_server.realtime.protocol.ServerMessage;

/**
 * 다른 모듈(게임·로비·채팅)이 실시간 신호를 보낼 때 쓰는 입구. (4-4~4-6에서 사용)
 *
 * 받는 쪽이 지금 연결돼 있지 않으면 조용히 버린다. 신호는 "다시 가져가라"는 알림일 뿐이고,
 * 클라이언트는 다시 연결될 때 GET으로 상태를 맞추므로 쌓아 두지 않는다.
 *
 * 구현
 *  - LocalRealtimeNotifier (4단계): 이 서버의 연결 목록으로 바로 보낸다. 서버 1대 기준
 *  - 5단계: Redis Pub/Sub으로 모든 서버에 뿌리고, 각 서버가 자기 연결 목록으로 보낸다
 * 보내는 쪽 코드는 이 인터페이스만 알므로 5단계에서 바뀌지 않는다.
 */
public interface RealtimeNotifier {

    /** 방을 구독 중인 모든 연결에 보낸다 */
    void sendToRoom(long roomId, ServerMessage message);

    /** 사용자의 모든 연결에 보낸다 (방 구독과 상관없이) */
    void sendToUser(long userId, ServerMessage message);

    /**
     * 사용자의 연결들을 그 방 구독에서 뺀다. 퇴장·강퇴·연결 끊김으로 방에서 빠졌을 때.
     * 보내기와 같은 인터페이스에 둔 이유: 서버가 여러 대면 그 사용자의 연결이 다른 서버에 있을 수 있어,
     * 5단계에서 보내기와 똑같이 모든 서버에 전달해야 하기 때문이다.
     */
    void removeUserFromRoom(long roomId, long userId);

    /** 그 방의 구독을 모두 푼다. 방이 삭제됐을 때. (모든 서버에 전달되는 이유는 위와 같다) */
    void removeRoom(long roomId);
}
