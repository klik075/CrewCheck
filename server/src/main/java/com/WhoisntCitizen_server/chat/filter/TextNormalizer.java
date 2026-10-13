package com.WhoisntCitizen_server.chat.filter;

import java.util.ArrayList;
import java.util.Arrays;
import java.util.List;

/**
 * 욕설 검사용 정규화. 원문 글자를 "비교용 토큰" 줄로 바꾸고, 토큰마다 원문 어디에서 왔는지(start~end)를 기억한다.
 * 그래서 정규화된 줄에서 찾은 욕설을 원문 위치로 되돌려 그 부분만 가릴 수 있다.
 *
 * 한글 줄(korean)
 *  - 완성형 글자는 초성/중성/종성 토큰으로 나눈다. 초성 ㅅ과 종성 ㅅ은 다른 토큰이다
 *    (그래서 "ㅅㅂ"이 "옷보다"의 '옷'+'보'에 걸리지 않는다)
 *  - 따로 친 자모(ㅅ, ㅂ, ㅄ…)와 영문 자판으로 친 한글(tlqkf → 시발)은 앞뒤를 보고 초성/종성을 정한다
 *  - 공백·숫자·특수문자는 건너뛴다 (시.1.발 → 시발, 시 발 → 시발)
 *  - 같은 토큰이 이어지면 하나로 줄인다 (ㅅㅅㅂㅂ → ㅅㅂ), 늘여 쓴 모음도 줄인다 (씨이이발 → 씨발, 바알 → 발)
 *
 * 영문 줄(latin)
 *  - 소문자로 바꾸고 흔한 치환을 되돌린다 (0→o, 1→i, 3→e, 4→a, 5→s, 7→t, @→a, $→s, !→i, |→l)
 *  - 공백·특수문자는 건너뛰고, 같은 글자가 이어지면 하나로 줄인다 (fuuuck → fuck)
 *  - 한글을 만나면 끊는다
 *
 * 사전 단어도 같은 정규화를 거치므로 사전에는 평소대로 쓰면 된다. (asshole → ashole로 같이 바뀐다)
 */
final class TextNormalizer {

    /** 어떤 사전 단어와도 이어지지 않게 끊는 토큰 */
    static final int BREAK = 1;

    private static final int CHO_BASE = 0x1100;
    private static final int JUNG_BASE = 0x1161;
    private static final int JONG_BASE = 0x11A7; // 종성 인덱스 1~27

    private static final String CHO = "ㄱㄲㄴㄷㄸㄹㅁㅂㅃㅅㅆㅇㅈㅉㅊㅋㅌㅍㅎ";
    private static final String JUNG = "ㅏㅐㅑㅒㅓㅔㅕㅖㅗㅘㅙㅚㅛㅜㅝㅞㅟㅠㅡㅢㅣ";
    private static final int CHO_IEUNG = 11; // ㅇ

    /** 초성 인덱스 → 종성 인덱스 (종성으로 못 쓰는 ㄸ·ㅃ·ㅉ은 0) */
    private static final int[] CHO_TO_JONG = {1, 2, 4, 7, 0, 8, 16, 17, 0, 19, 20, 21, 22, 0, 23, 24, 25, 26, 27};

    /** 따로 친 겹자음 → 두 자음 */
    private static final String COMPOUND_CONSONANTS = "ㄳㄵㄶㄺㄻㄼㄽㄾㄿㅀㅄ";
    private static final String[] COMPOUND_PARTS = {"ㄱㅅ", "ㄴㅈ", "ㄴㅎ", "ㄹㄱ", "ㄹㅁ", "ㄹㅂ", "ㄹㅅ", "ㄹㅌ", "ㄹㅍ", "ㄹㅎ", "ㅂㅅ"};

    /** 두벌식 자판: 영문 글자 → 자모 */
    private static final String KEYS = "qwertyuiopasdfghjklzxcvbnmQWERTOP";
    private static final String KEY_JAMO = "ㅂㅈㄷㄱㅅㅛㅕㅑㅐㅔㅁㄴㅇㄹㅎㅗㅓㅏㅣㅋㅌㅊㅍㅠㅜㅡㅃㅉㄸㄲㅆㅒㅖ";

    private TextNormalizer() {
    }

    /** 정규화 결과. codes[i] 토큰은 원문 start[i]~end[i](포함)에서 왔다 */
    record Normalized(int[] codes, int[] start, int[] end) {
        int length() {
            return codes.length;
        }
    }

    static boolean containsHangul(String s) {
        for (int i = 0; i < s.length(); i++) {
            if (isSyllable(s.charAt(i)) || isCompatJamo(s.charAt(i))) return true;
        }
        return false;
    }

    // ---------------- 한글 줄 ----------------

    private enum Kind { SYLLABLE, CONSONANT, VOWEL, SKIP, SPACE }

    /** 1차 분류: 원문 한 글자(겹자음은 두 개)를 한 항목으로 */
    private record Item(Kind kind, int value, boolean fromLatin, int index) {
    }

    static Normalized korean(String text) {
        List<Item> items = classify(text);
        Builder out = new Builder(text.length());
        for (int k = 0; k < items.size(); k++) {
            Item it = items.get(k);
            switch (it.kind()) {
                case SYLLABLE -> addSyllable(out, it.value(), it.index());
                case CONSONANT -> {
                    Item next = neighbor(items, k, +1);
                    Item prev = neighbor(items, k, -1);
                    int jong = CHO_TO_JONG[it.value()];
                    if (next != null && next.kind() == Kind.VOWEL) {
                        out.add(CHO_BASE + it.value(), it.index());
                    } else if (prev != null && prev.kind() == Kind.VOWEL && jong > 0) {
                        out.add(JONG_BASE + jong, it.index());
                    } else if (it.fromLatin()) {
                        // 영문 자판의 외톨이 자음은 한글로 볼 근거가 없다 ("news"의 ws가 ㅈㄴ이 되지 않게)
                        out.add(BREAK, it.index());
                    } else {
                        out.add(CHO_BASE + it.value(), it.index()); // ㅅㅂ처럼 초성만 친 경우
                    }
                }
                case VOWEL -> {
                    Item next = k + 1 < items.size() ? items.get(k + 1) : null;
                    int combined = next != null && next.kind() == Kind.VOWEL ? combineVowels(it.value(), next.value()) : -1;
                    if (combined >= 0) {
                        out.add(JUNG_BASE + combined, it.index());
                        out.extendLast(next.index());
                        k++;
                    } else {
                        out.add(JUNG_BASE + it.value(), it.index());
                    }
                }
                case SKIP, SPACE -> {
                    // 토큰을 만들지 않는다. 사이에 낀 글자는 앞뒤 토큰이 함께 걸리면 같이 순화 단어로 바뀐다
                }
            }
        }
        return out.build();
    }

    private static List<Item> classify(String text) {
        List<Item> items = new ArrayList<>(text.length());
        for (int i = 0; i < text.length(); i++) {
            char c = text.charAt(i);
            if (isSyllable(c)) {
                items.add(new Item(Kind.SYLLABLE, c - 0xAC00, false, i));
            } else if (isCompatJamo(c)) {
                int cho = CHO.indexOf(c);
                int jung = JUNG.indexOf(c);
                int compound = COMPOUND_CONSONANTS.indexOf(c);
                if (cho >= 0) {
                    items.add(new Item(Kind.CONSONANT, cho, false, i));
                } else if (jung >= 0) {
                    items.add(new Item(Kind.VOWEL, jung, false, i));
                } else if (compound >= 0) {
                    for (char part : COMPOUND_PARTS[compound].toCharArray()) {
                        items.add(new Item(Kind.CONSONANT, CHO.indexOf(part), false, i));
                    }
                } else {
                    items.add(new Item(Kind.SKIP, 0, false, i)); // 옛 한글 자모 등
                }
            } else if (KEYS.indexOf(c) >= 0 || KEYS.indexOf(Character.toLowerCase(c)) >= 0) {
                int key = KEYS.indexOf(c);
                if (key < 0) key = KEYS.indexOf(Character.toLowerCase(c)); // Shift 자모가 없는 대문자
                char jamo = KEY_JAMO.charAt(key);
                int cho = CHO.indexOf(jamo);
                items.add(cho >= 0
                        ? new Item(Kind.CONSONANT, cho, true, i)
                        : new Item(Kind.VOWEL, JUNG.indexOf(jamo), true, i));
            } else if (Character.isWhitespace(c)) {
                items.add(new Item(Kind.SPACE, 0, false, i));
            } else {
                items.add(new Item(Kind.SKIP, 0, false, i)); // 숫자·특수문자·이모지 등
            }
        }
        return items;
    }

    /** 자음의 초성/종성을 정할 때 보는 옆 항목. 특수문자는 건너뛰고, 공백·완성형 글자에서는 멈춘다 */
    private static Item neighbor(List<Item> items, int k, int step) {
        for (int j = k + step; j >= 0 && j < items.size(); j += step) {
            Item it = items.get(j);
            if (it.kind() == Kind.SKIP) continue;
            return (it.kind() == Kind.CONSONANT || it.kind() == Kind.VOWEL) ? it : null;
        }
        return null;
    }

    private static void addSyllable(Builder out, int s, int index) {
        int cho = s / 588;
        int jung = (s % 588) / 28;
        int jong = s % 28;
        // 늘여 쓴 모음: 앞 글자가 받침 없이 같은 모음으로 끝났고 이 글자가 'ㅇ+같은 모음'이면 ㅇ과 모음은 버린다
        // (씨이이발 → 씨발, 바알 → 발)
        if (cho == CHO_IEUNG && out.lastCode() == JUNG_BASE + jung) {
            if (jong > 0) {
                out.add(JONG_BASE + jong, index);
            } else {
                out.extendLast(index);
            }
            return;
        }
        out.add(CHO_BASE + cho, index);
        out.add(JUNG_BASE + jung, index);
        if (jong > 0) out.add(JONG_BASE + jong, index);
    }

    /** 영문 자판으로 친 겹모음 (hk → ㅘ 등). 못 합치면 -1 */
    private static int combineVowels(int a, int b) {
        String pair = "" + JUNG.charAt(a) + JUNG.charAt(b);
        return switch (pair) {
            case "ㅗㅏ" -> JUNG.indexOf('ㅘ');
            case "ㅗㅐ" -> JUNG.indexOf('ㅙ');
            case "ㅗㅣ" -> JUNG.indexOf('ㅚ');
            case "ㅜㅓ" -> JUNG.indexOf('ㅝ');
            case "ㅜㅔ" -> JUNG.indexOf('ㅞ');
            case "ㅜㅣ" -> JUNG.indexOf('ㅟ');
            case "ㅡㅣ" -> JUNG.indexOf('ㅢ');
            default -> -1;
        };
    }

    // ---------------- 영문 줄 ----------------

    static Normalized latin(String text) {
        Builder out = new Builder(text.length());
        for (int i = 0; i < text.length(); i++) {
            char c = Character.toLowerCase(text.charAt(i));
            char mapped = switch (c) {
                case '0' -> 'o';
                case '1', '!' -> 'i';
                case '3' -> 'e';
                case '4', '@' -> 'a';
                case '5', '$' -> 's';
                case '7' -> 't';
                case '|' -> 'l';
                default -> c;
            };
            if (mapped >= 'a' && mapped <= 'z') {
                out.add(mapped, i);
            } else if (isSyllable(c) || isCompatJamo(c)) {
                out.add(BREAK, i);
            }
            // 그 밖(공백·특수문자)은 건너뛴다
        }
        return out.build();
    }

    // ---------------- 공통 ----------------

    private static boolean isSyllable(char c) {
        return c >= 0xAC00 && c <= 0xD7A3;
    }

    private static boolean isCompatJamo(char c) {
        return c >= 0x3131 && c <= 0x318E;
    }

    /** 토큰을 모으며, 같은 토큰이 이어지면 하나로 줄이고 원문 범위만 늘린다 */
    private static final class Builder {
        private int[] codes;
        private int[] start;
        private int[] end;
        private int size;

        Builder(int capacity) {
            int n = Math.max(4, capacity * 3);
            codes = new int[n];
            start = new int[n];
            end = new int[n];
        }

        int lastCode() {
            return size == 0 ? 0 : codes[size - 1];
        }

        void add(int code, int index) {
            if (size > 0 && codes[size - 1] == code) {
                end[size - 1] = index; // 반복 → 하나로
                return;
            }
            if (size == codes.length) grow();
            codes[size] = code;
            start[size] = index;
            end[size] = index;
            size++;
        }

        /** 마지막 토큰이 원문 index까지 이어진 것으로 본다 */
        void extendLast(int index) {
            if (size > 0) end[size - 1] = index;
        }

        private void grow() {
            int n = codes.length * 2;
            codes = Arrays.copyOf(codes, n);
            start = Arrays.copyOf(start, n);
            end = Arrays.copyOf(end, n);
        }

        Normalized build() {
            return new Normalized(Arrays.copyOf(codes, size), Arrays.copyOf(start, size), Arrays.copyOf(end, size));
        }
    }
}
