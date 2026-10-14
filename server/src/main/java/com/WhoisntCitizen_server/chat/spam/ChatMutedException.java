package com.WhoisntCitizen_server.chat.spam;

/**
 * 도배로 채팅이 금지됨 → 429 Too Many Requests, code = CHAT_MUTED (GlobalExceptionHandler)
 *  - 금지가 막 시작된 경우(같은 내용 muteAt번째): warning에 경고 문구, message에 채팅 금지 안내
 *  - 이미 금지 중인 경우: warning은 null, message에 남은 시간 안내
 * 이 메시지는 저장되지 않는다.
 */
public class ChatMutedException extends RuntimeException {

    public static final String CODE = "CHAT_MUTED";

    private final String warning;
    private final long remainingSeconds;
    private final boolean justMuted;

    public ChatMutedException(String message, String warning, long remainingSeconds, boolean justMuted) {
        super(message);
        this.warning = warning;
        this.remainingSeconds = remainingSeconds;
        this.justMuted = justMuted;
    }

    /** 경고 문구 (금지가 막 시작된 경우만, 아니면 null) */
    public String getWarning() {
        return warning;
    }

    /** 채팅 금지가 풀리기까지 남은 시간(초, 올림) */
    public long getRemainingSeconds() {
        return remainingSeconds;
    }

    /** 이번 메시지로 채팅 금지가 시작됐는지 */
    public boolean isJustMuted() {
        return justMuted;
    }
}
