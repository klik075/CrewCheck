package com.WhoisntCitizen_server.realtime.protocol;

/** 클라이언트 메시지를 해석하지 못했다. 받는 쪽은 error 메시지로 답하고 연결은 유지한다 */
public class RealtimeMessageException extends RuntimeException {

    private final RealtimeErrorCode code;

    public RealtimeMessageException(RealtimeErrorCode code, String message) {
        super(message);
        this.code = code;
    }

    public RealtimeErrorCode code() {
        return code;
    }

    /** 클라이언트에게 돌려줄 error 메시지 */
    public ServerMessage.ErrorMessage toMessage() {
        return new ServerMessage.ErrorMessage(code, getMessage());
    }
}
