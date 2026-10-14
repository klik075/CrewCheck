package com.WhoisntCitizen_server.chat.spam;

/**
 * 사용자별 도배 기록 저장소. 확인과 기록을 한 번에(원자적으로) 처리한다.
 * 구현: RedisChatSpamStore(서버 여러 대가 함께 씀), InMemoryChatSpamStore(테스트·Redis 없이 실행)
 */
public interface ChatSpamStore {

    /**
     * 사용자가 contentKey 내용을 보내려 한다.
     *  - 이미 채팅 금지 중이면 기록하지 않고 alreadyMuted 결과를 돌려준다
     *  - 직전 메시지와 같은 내용이고 repeatWindow 안이면 횟수 + 1, 아니면 1
     *  - 횟수가 muteAt에 닿으면 횟수를 지우고 muteDuration 동안 채팅을 금지한다
     */
    SpamRecord record(long userId, String contentKey, ChatSpamPolicy policy);

    /**
     * @param repeatCount      같은 내용을 연속으로 보낸 횟수 (이번 메시지 포함). 이미 금지 중이면 0
     * @param muteRemainingMs  채팅 금지 남은 시간(ms). 금지가 아니면 0
     * @param alreadyMuted     이번 메시지 전에 이미 채팅 금지 중이었는지
     */
    record SpamRecord(int repeatCount, long muteRemainingMs, boolean alreadyMuted) {

        public static SpamRecord muted(long remainingMs) {
            return new SpamRecord(0, remainingMs, true);
        }

        public static SpamRecord counted(int count, long newMuteMs) {
            return new SpamRecord(count, newMuteMs, false);
        }

        /** 이번 메시지로 채팅 금지가 시작됐는지 */
        public boolean justMuted() {
            return !alreadyMuted && muteRemainingMs > 0;
        }
    }
}
