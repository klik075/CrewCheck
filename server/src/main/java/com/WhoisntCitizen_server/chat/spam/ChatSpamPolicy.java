package com.WhoisntCitizen_server.chat.spam;

import java.time.Duration;

/**
 * 도배 방지 규칙 (설정 chat.spam.*)
 *  - warnAt        같은 내용을 이 횟수째 보낼 때부터 매번 경고한다 (기본 3)
 *  - muteAt        같은 내용을 이 횟수째 보내면 그 메시지는 막고 채팅을 금지한다 (기본 10)
 *  - muteDuration  채팅 금지 시간 (기본 3분)
 *  - repeatWindow  같은 내용 사이의 간격이 이보다 길면 횟수를 1부터 다시 센다 (기본 60초)
 *
 * "같은 내용"은 연속으로 보낸 메시지끼리만 비교한다. 다른 내용을 한 번 보내면 횟수는 다시 1부터 센다.
 */
public record ChatSpamPolicy(int warnAt, int muteAt, Duration muteDuration, Duration repeatWindow) {

    public static final ChatSpamPolicy DEFAULT =
            new ChatSpamPolicy(3, 10, Duration.ofMinutes(3), Duration.ofSeconds(60));

    public ChatSpamPolicy {
        if (warnAt < 2) throw new IllegalArgumentException("chat.spam.warn-at은 2 이상이어야 합니다.");
        if (muteAt <= warnAt) throw new IllegalArgumentException("chat.spam.mute-at은 warn-at보다 커야 합니다.");
        if (muteDuration == null || muteDuration.isNegative() || muteDuration.isZero()) {
            throw new IllegalArgumentException("chat.spam.mute-seconds는 1 이상이어야 합니다.");
        }
        if (repeatWindow == null || repeatWindow.isNegative() || repeatWindow.isZero()) {
            throw new IllegalArgumentException("chat.spam.repeat-window-seconds는 1 이상이어야 합니다.");
        }
    }
}
