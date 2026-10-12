package com.WhoisntCitizen_server.realtime;

/**
 * WebSocket 연결이 살아 있다는 것을 접속 기록(연결 끊김 판정)에 남긴다. ping을 받을 때 부른다.
 * polling을 하지 않는 클라이언트도 ping만 보내면 게임에서 연결이 끊긴 것으로 처리되지 않는다.
 */
public interface RealtimeActivityRecorder {

    /** 이 사용자가 구독 중인 방(roomId)에서 진행 중인 게임이 있으면 접속 시각을 남긴다 */
    void recordAlive(long roomId, long userId);
}
