package com.WhoisntCitizen_server.realtime.protocol;

import java.time.Duration;

/**
 * WebSocket(실시간 알림) 규칙의 고정 값. 자세한 설명은 Claude outputs/websocket-protocol.md.
 *
 * 역할 나누기
 *  - WebSocket: 서버 → 클라이언트 "바뀌었어" 신호만 보낸다. (내용 없음)
 *  - REST: 신호를 받은 클라이언트가 지금까지 쓰던 GET으로 내용을 가져온다. 요청(투표·밤 행동·채팅 보내기)도 그대로 REST다.
 *  그래서 보이는 정보 처리(역할 비공개, 해적 전용 등)는 REST 응답 한곳에만 있고, 신호를 놓쳐도 다음 GET이 최신 상태를 가져온다.
 */
public final class RealtimeProtocol {

    /** 연결 주소. 규칙이 크게 바뀌면 /ws/v2를 새로 연다 */
    public static final String PATH = "/ws/v1";

    /**
     * 토큰을 Authorization 헤더에 못 넣는 클라이언트(브라우저·WebGL)를 위한 쿼리 이름. 예) /ws/v1?token=eyJ...
     * 헤더가 있으면 헤더를 먼저 쓴다. 주소에 토큰이 들어가므로 서버는 연결 주소 전체를 로그에 남기지 않는다.
     */
    public static final String TOKEN_QUERY_PARAM = "token";

    /** 클라이언트가 ping을 보내는 간격. 서버는 ping을 받을 때 접속 기록(touch)을 남긴다 */
    public static final Duration HEARTBEAT_INTERVAL = Duration.ofSeconds(10);

    /** 이 시간 동안 아무 메시지(ping 포함)도 없으면 서버가 연결을 닫는다 (ping 3번 놓침) */
    public static final Duration IDLE_TIMEOUT = Duration.ofSeconds(30);

    /** 클라이언트 → 서버 메시지 한 개의 최대 길이(문자). 넘으면 BAD_MESSAGE */
    public static final int MAX_CLIENT_MESSAGE_CHARS = 4096;

    /** 서버가 연결을 닫는 이유 코드 (WebSocket close code, 4000~4999는 앱이 정해 쓰는 범위) */
    public static final int CLOSE_IDLE_TIMEOUT = 4408;
    /** 연결 중 토큰이 만료됨. 클라이언트는 다시 로그인(토큰 갱신)한 뒤 연결한다 */
    public static final int CLOSE_TOKEN_EXPIRED = 4401;

    private RealtimeProtocol() {
    }
}
