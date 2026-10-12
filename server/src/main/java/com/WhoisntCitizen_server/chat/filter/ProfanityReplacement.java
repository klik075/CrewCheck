package com.WhoisntCitizen_server.chat.filter;

/**
 * 걸린 욕설을 바꿔 넣을 순화 단어. 걸린 부분의 글자 수(공백 제외)에 따라 정한다.
 *
 *   글자 수   | 바꿔 넣는 말
 *   ---------+--------------
 *   2 이하   | 땡큐
 *   3        | 좋아요
 *   4        | 좋은친구
 *   5        | 감사합니다
 *   6 이상   | 사이좋은친구
 *
 * 글자 수는 원문에서 걸린 범위를 센다. 그래서 끼워 넣은 숫자·특수문자도 포함된다.
 * (시발 → 땡큐, 시1발 → 좋아요, tlqkf → 감사합니다, 시 발 → 땡큐: 공백은 세지 않는다)
 */
public final class ProfanityReplacement {

    static final String UP_TO_2 = "땡큐";
    static final String LENGTH_3 = "좋아요";
    static final String LENGTH_4 = "좋은친구";
    static final String LENGTH_5 = "감사합니다";
    static final String FROM_6 = "사이좋은친구";

    private ProfanityReplacement() {
    }

    /** 글자 수에 맞는 순화 단어 */
    public static String forLength(int length) {
        if (length <= 2) return UP_TO_2;
        return switch (length) {
            case 3 -> LENGTH_3;
            case 4 -> LENGTH_4;
            case 5 -> LENGTH_5;
            default -> FROM_6;
        };
    }

    /** 원문에서 걸린 부분을 받아 바꿔 넣을 말을 돌려준다. 공백은 세지 않고, 이모지 같은 글자도 한 글자로 센다 */
    public static String replace(String matched) {
        int length = 0;
        for (int i = 0; i < matched.length(); ) {
            int cp = matched.codePointAt(i);
            if (!Character.isWhitespace(cp)) length++;
            i += Character.charCount(cp);
        }
        return forLength(length);
    }
}
