package com.WhoisntCitizen_server.realtime.protocol;

/** error 메시지의 code. JSON에는 이름 그대로 들어간다 (예: "NOT_IN_ROOM") */
public enum RealtimeErrorCode {
    /** JSON이 아니거나, 필요한 값이 없거나, 너무 길다 */
    BAD_MESSAGE,
    /** 모르는 type */
    UNKNOWN_TYPE,
    /** 들어 있지 않은 방을 구독하려 했다 */
    NOT_IN_ROOM
}
