package com.WhoisntCitizen_server.chat.spam;

import java.nio.charset.StandardCharsets;
import java.security.MessageDigest;
import java.security.NoSuchAlgorithmException;
import java.util.HexFormat;
import java.util.Locale;

/**
 * 채팅 도배 방지.
 *  - 같은 내용을 연속으로 warnAt(3)번째 보낼 때부터 보낼 때마다 경고 문구를 돌려준다. (메시지는 그대로 저장)
 *  - muteAt(10)번째에는 그 메시지를 막고 muteDuration(3분) 동안 채팅을 금지한다. (경고 문구 + 채팅 금지 안내)
 *  - 금지 중에 보낸 메시지는 모두 막고 남은 시간을 안내한다.
 * "같은 내용"은 앞뒤·중간 공백과 영문 대소문자를 무시하고 비교한다. ("ㅋㅋ ㅋ" = "ㅋㅋㅋ", "GG" = "gg")
 * 다른 내용을 보내거나 repeatWindow(60초) 넘게 쉬면 횟수는 1부터 다시 센다.
 */
public final class ChatSpamGuard {

    private static final ChatSpamGuard DISABLED = new ChatSpamGuard(null, ChatSpamPolicy.DEFAULT);

    private final ChatSpamStore store;
    private final ChatSpamPolicy policy;

    private ChatSpamGuard(ChatSpamStore store, ChatSpamPolicy policy) {
        this.store = store;
        this.policy = policy;
    }

    public static ChatSpamGuard of(ChatSpamStore store, ChatSpamPolicy policy) {
        if (store == null) throw new IllegalArgumentException("store가 필요합니다.");
        return new ChatSpamGuard(store, policy == null ? ChatSpamPolicy.DEFAULT : policy);
    }

    /** 아무것도 막지 않는 가드 (chat.spam.enabled=false, 도배 방지와 관계없는 테스트) */
    public static ChatSpamGuard disabled() {
        return DISABLED;
    }

    public boolean isEnabled() {
        return store != null;
    }

    public ChatSpamPolicy policy() {
        return policy;
    }

    /**
     * 사용자 메시지를 보내기 직전에 부른다.
     * @return 보낼 수 있으면 경고 문구(경고가 없으면 null)
     * @throws ChatMutedException 채팅 금지 중이거나 이번 메시지로 금지가 시작된 경우 (메시지를 저장하지 않는다)
     */
    public String check(long userId, String message) {
        if (store == null) return null;
        String key = contentKey(message);
        if (key.isEmpty()) return null;

        ChatSpamStore.SpamRecord record = store.record(userId, key, policy);
        if (record.alreadyMuted()) {
            long seconds = ceilSeconds(record.muteRemainingMs());
            throw new ChatMutedException(mutedMessage(seconds), null, seconds, false);
        }
        if (record.justMuted()) {
            long seconds = ceilSeconds(record.muteRemainingMs());
            throw new ChatMutedException(justMutedMessage(), warningMessage(record.repeatCount()), seconds, true);
        }
        return record.repeatCount() >= policy.warnAt() ? warningMessage(record.repeatCount()) : null;
    }

    // ---------- 문구 ----------

    /** 경고 (주황색): "같은 내용을 3번 연속으로 보냈습니다. 10번째에는 3분간 채팅이 금지됩니다. (3/10)" */
    String warningMessage(int count) {
        if (count >= policy.muteAt()) {
            return "같은 내용을 " + count + "번 연속으로 보냈습니다. (" + count + "/" + policy.muteAt() + ")";
        }
        return "같은 내용을 " + count + "번 연속으로 보냈습니다. 도배를 계속하면 " + policy.muteAt() + "번째에 "
                + formatDuration(policy.muteDuration().toSeconds()) + "간 채팅이 금지됩니다. ("
                + count + "/" + policy.muteAt() + ")";
    }

    /** 채팅 금지 시작 (붉은색): "도배로 3분간 채팅이 금지되었습니다." */
    String justMutedMessage() {
        return "도배로 " + formatDuration(policy.muteDuration().toSeconds()) + "간 채팅이 금지되었습니다.";
    }

    /** 채팅 금지 중 (붉은색): "도배로 채팅이 금지된 상태입니다. 2분 15초 후에 다시 채팅할 수 있습니다." */
    static String mutedMessage(long remainingSeconds) {
        return "도배로 채팅이 금지된 상태입니다. " + formatDuration(remainingSeconds) + " 후에 다시 채팅할 수 있습니다.";
    }

    /** 125 → "2분 5초", 180 → "3분", 40 → "40초" */
    static String formatDuration(long seconds) {
        long s = Math.max(1, seconds);
        long m = s / 60, r = s % 60;
        if (m == 0) return r + "초";
        return r == 0 ? m + "분" : m + "분 " + r + "초";
    }

    private static long ceilSeconds(long millis) {
        return Math.max(1, (millis + 999) / 1000);
    }

    // ---------- 같은 내용 판단 ----------

    /** 공백을 모두 빼고 소문자로 바꾼 내용의 SHA-256 (Redis에 원문을 남기지 않기 위해 해시로 저장) */
    static String contentKey(String message) {
        if (message == null) return "";
        String normalized = message.replaceAll("\\s+", "").toLowerCase(Locale.ROOT);
        if (normalized.isEmpty()) return "";
        try {
            byte[] hash = MessageDigest.getInstance("SHA-256").digest(normalized.getBytes(StandardCharsets.UTF_8));
            return HexFormat.of().formatHex(hash);
        } catch (NoSuchAlgorithmException e) {
            return normalized; // SHA-256은 모든 JVM에 있으므로 오지 않는다
        }
    }

    @Override
    public String toString() {
        return store == null ? "ChatSpamGuard(disabled)"
                : "ChatSpamGuard(warnAt=" + policy.warnAt() + ", muteAt=" + policy.muteAt()
                  + ", mute=" + policy.muteDuration().toSeconds() + "s, window="
                  + policy.repeatWindow().toSeconds() + "s, store=" + store.getClass().getSimpleName() + ")";
    }
}
