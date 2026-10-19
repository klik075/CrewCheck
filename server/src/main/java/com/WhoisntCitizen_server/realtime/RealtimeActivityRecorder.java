package com.WhoisntCitizen_server.realtime;

/**
 * WebSocket 연결이 살아 있다는 것을 접속 기록(연결 끊김 판정)에 남긴다. ping을 받을 때와 방 구독이 받아들여졌을 때 부른다.
 * polling을 하지 않는 클라이언트도 ping만 보내면 게임·대기실에서 연결이 끊긴 것으로 처리되지 않는다.
 */
public interface RealtimeActivityRecorder {

    /** 이 사용자가 구독 중인 방(roomId)의 접속 기록을 남긴다. 게임 중이면 게임 기록, 대기 중이면 대기실 기록 */
    void recordAlive(long roomId, long userId);
}
