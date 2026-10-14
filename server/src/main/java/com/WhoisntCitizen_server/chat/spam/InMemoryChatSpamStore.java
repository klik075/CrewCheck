package com.WhoisntCitizen_server.chat.spam;

import java.time.Clock;
import java.util.Map;
import java.util.concurrent.ConcurrentHashMap;

/**
 * 서버 메모리에 두는 도배 기록. (테스트, Redis 없이 실행할 때)
 * 서버가 여러 대면 서버마다 따로 세므로 운영에서는 RedisChatSpamStore를 쓴다.
 */
public class InMemoryChatSpamStore implements ChatSpamStore {

    private final Clock clock;
    private final Map<Long, State> states = new ConcurrentHashMap<>();

    public InMemoryChatSpamStore() {
        this(Clock.systemUTC());
    }

    public InMemoryChatSpamStore(Clock clock) {
        this.clock = clock;
    }

    /** 사용자별 상태: 금지 끝 시각, 직전 내용, 연속 횟수, 직전 메시지 시각 */
    private static final class State {
        long mutedUntil;
        String lastKey;
        int count;
        long lastAt;
    }

    @Override
    public SpamRecord record(long userId, String contentKey, ChatSpamPolicy policy) {
        long now = clock.millis();
        if (states.size() > PRUNE_THRESHOLD) prune(now, policy);
        State s = states.computeIfAbsent(userId, id -> new State());
        synchronized (s) {
            if (s.mutedUntil > now) {
                return SpamRecord.muted(s.mutedUntil - now);
            }
            boolean sameStreak = contentKey.equals(s.lastKey) && now - s.lastAt <= policy.repeatWindow().toMillis();
            int count = sameStreak ? s.count + 1 : 1;

            if (count >= policy.muteAt()) {
                long muteMs = policy.muteDuration().toMillis();
                s.mutedUntil = now + muteMs;
                s.lastKey = null;
                s.count = 0;
                s.lastAt = 0;
                return SpamRecord.counted(count, muteMs);
            }
            s.lastKey = contentKey;
            s.count = count;
            s.lastAt = now;
            return SpamRecord.counted(count, 0);
        }
    }

    /** 사용자가 많이 쌓이면 금지도 끝났고 연속 기록도 만료된 사용자를 지운다 */
    private static final int PRUNE_THRESHOLD = 10_000;

    private void prune(long now, ChatSpamPolicy policy) {
        long window = policy.repeatWindow().toMillis();
        states.entrySet().removeIf(e -> {
            State s = e.getValue();
            synchronized (s) {
                return s.mutedUntil <= now && now - s.lastAt > window;
            }
        });
    }
}
