package com.WhoisntCitizen_server.realtime;

import java.util.ArrayList;
import java.util.List;

/** 테스트용 연결. 보낸 JSON을 쌓아 둔다 */
class FakeConnection implements RealtimeConnection {

    private final String id;
    private final long userId;
    final List<String> sent = new ArrayList<>();
    boolean open = true;
    boolean failOnSend = false;
    /** close로 닫혔으면 그 코드, 아니면 -1 */
    int closedCode = -1;

    FakeConnection(String id, long userId) {
        this.id = id;
        this.userId = userId;
    }

    @Override
    public String id() {
        return id;
    }

    @Override
    public long userId() {
        return userId;
    }

    @Override
    public boolean isOpen() {
        return open;
    }

    @Override
    public void send(String json) {
        if (failOnSend) {
            throw new IllegalStateException("보내기 실패 (테스트)");
        }
        sent.add(json);
    }

    @Override
    public void close(int code, String reason) {
        closedCode = code;
        open = false;
    }
}
