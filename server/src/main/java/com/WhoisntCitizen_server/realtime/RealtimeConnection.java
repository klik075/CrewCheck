package com.WhoisntCitizen_server.realtime;

/**
 * 클라이언트 연결 하나. 연결 목록(RealtimeSessionRegistry)은 WebSocket을 몰라도 되도록 이 인터페이스만 다룬다.
 * 실제 구현은 WebSocketRealtimeConnection, 테스트에서는 가짜 연결을 쓴다.
 */
public interface RealtimeConnection {

    /** 연결 id (WebSocket 세션 id). 서버 안에서 유일하다 */
    String id();

    /** 이 연결로 인증된 사용자 id */
    long userId();

    boolean isOpen();

    /**
     * 이미 JSON으로 바꾼 메시지를 보낸다. 실패해도 예외를 던지지 않는다.
     * (한 연결의 실패 때문에 같은 방의 다른 연결에 못 보내는 일이 없게)
     */
    void send(String json);

    /** 서버 쪽에서 연결을 닫는다. code는 WebSocket close code (예: 4408 무응답, 4401 토큰 만료) */
    void close(int code, String reason);
}
