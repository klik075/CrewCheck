package com.WhoisntCitizen_server.chat.filter;

import java.io.BufferedReader;
import java.io.IOException;
import java.io.InputStream;
import java.io.InputStreamReader;
import java.nio.charset.StandardCharsets;
import java.util.ArrayList;
import java.util.Collection;
import java.util.List;

/**
 * 채팅 욕설 검열. 걸린 부분을 글자 수에 맞는 순화 단어로 바꾼다. (땡큐/좋아요/좋은친구/감사합니다/사이좋은친구, ProfanityReplacement)
 *
 * 처리 순서
 *  1. 정규화(TextNormalizer): 한글 줄과 영문 줄을 만든다. 띄어쓰기·특수문자 끼워 넣기, 초성만 쓰기,
 *     영문 자판으로 치기(tlqkf), 글자 반복·늘여 쓰기, 숫자·기호 치환(f4ck)을 되돌린다
 *  2. 금칙어 찾기(PatternMatcher, Aho-Corasick): 사전 단어를 한 번에 찾는다
 *  3. 걸러 내기
 *     - 허용어: 걸린 금칙어가 허용어 안에 통째로 들어 있으면 넘어간다 (시발점, 시바견, 병신년…)
 *     - 띄어쓰기를 넘어 걸린 경우: 단어 경계에서 시작하고 끝날 때만 욕설로 본다
 *       ("시 발"·"씨 발 놈"은 걸리고, "한시 발표"·"수박씨 발라"는 걸리지 않는다)
 *     - 글자 중간에서 끝난 경우: "시바"가 "시방"·"씨받이"의 앞부분에 걸린 것처럼 받침 앞에서 끊기면 넘어간다
 *     - 사전에 =단어 로 적은 단어는 낱말 전체일 때만 걸린다 (=cunt → "scunthorpe"는 통과)
 *  4. 바꾸기: 겹치거나 맞닿은 범위는 한 곳으로 합친 뒤, 그 원문 부분을 순화 단어 하나로 바꾼다
 *     (시발 → 땡큐, 개새끼 → 좋아요, 시발병신 → 좋은친구. 범위 안의 공백도 함께 바뀐다: 시 발 → 땡큐)
 *
 * 만든 뒤에는 읽기만 하므로 여러 스레드에서 같이 써도 된다.
 * 사전은 resources/profanity/banned-words.txt, allowed-words.txt (ProfanityFilterConfig가 읽는다)
 */
public final class ProfanityFilter {

    private static final ProfanityFilter DISABLED = new ProfanityFilter(List.of(), List.of());

    private final PatternMatcher koreanBanned = new PatternMatcher();
    private final PatternMatcher koreanAllowed = new PatternMatcher();
    private final PatternMatcher latinBanned = new PatternMatcher();
    private final PatternMatcher latinAllowed = new PatternMatcher();

    /** 검사 결과. text = 바뀐 문장, matchCount = 바꾼 곳의 수 (겹치거나 맞닿아 걸린 금칙어는 한 곳으로 센다. 시발 = 시발+시바) */
    public record Result(String text, int matchCount) {
        public boolean detected() {
            return matchCount > 0;
        }
    }

    private ProfanityFilter(Collection<String> banned, Collection<String> allowed) {
        for (String word : banned) add(word, koreanBanned, latinBanned);
        for (String word : allowed) add(word, koreanAllowed, latinAllowed);
        koreanBanned.build();
        koreanAllowed.build();
        latinBanned.build();
        latinAllowed.build();
    }

    /** 금칙어·허용어 목록으로 만든다 */
    public static ProfanityFilter of(Collection<String> bannedWords, Collection<String> allowedWords) {
        return new ProfanityFilter(bannedWords, allowedWords);
    }

    /** 아무것도 바꾸지 않는 필터 (검열을 끈 경우, 테스트) */
    public static ProfanityFilter disabled() {
        return DISABLED;
    }

    /** 사전 파일에서 단어를 읽는다. UTF-8, 한 줄에 한 단어, # 뒤는 주석, 빈 줄은 무시. (=단어 표시는 그대로 둔다) */
    public static List<String> readWords(InputStream in) throws IOException {
        List<String> words = new ArrayList<>();
        try (BufferedReader reader = new BufferedReader(new InputStreamReader(in, StandardCharsets.UTF_8))) {
            String line;
            while ((line = reader.readLine()) != null) {
                int comment = line.indexOf('#');
                String word = (comment >= 0 ? line.substring(0, comment) : line).strip();
                if (word.startsWith("﻿")) word = word.substring(1).strip(); // BOM
                if (!word.isEmpty()) words.add(word);
            }
        }
        return words;
    }

    public boolean isEnabled() {
        return !koreanBanned.isEmpty() || !latinBanned.isEmpty();
    }

    /** 욕설을 순화 단어로 바꾼 문장. 걸린 게 없으면 원문 그대로 */
    public String mask(String text) {
        return filter(text).text();
    }

    public boolean containsProfanity(String text) {
        return filter(text).detected();
    }

    public Result filter(String text) {
        if (text == null || text.isEmpty() || !isEnabled()) return new Result(text, 0);
        List<int[]> spans = new ArrayList<>();
        collect(text, TextNormalizer.korean(text), koreanBanned, koreanAllowed, spans);
        collect(text, TextNormalizer.latin(text), latinBanned, latinAllowed, spans);
        if (spans.isEmpty()) return new Result(text, 0);

        List<int[]> regions = merge(spans);
        StringBuilder sb = new StringBuilder(text.length() + regions.size() * 4);
        int copied = 0; // 원문에서 여기까지는 이미 옮겼다
        for (int[] region : regions) {
            sb.append(text, copied, region[0]);                                    // 욕설 앞의 정상 부분
            sb.append(ProfanityReplacement.replace(text.substring(region[0], region[1] + 1))); // 욕설 → 순화 단어
            copied = region[1] + 1;
        }
        sb.append(text, copied, text.length());
        return new Result(sb.toString(), regions.size());
    }

    /**
     * 걸린 범위들을 시작 위치 순으로 정렬하고, 겹치거나 바로 맞닿은 범위는 하나로 합친다.
     * (시발 = '시바'와 '시발'이 겹쳐 걸림 → 한 곳, 시발병신 = 맞닿음 → 한 곳)
     * 합치지 않으면 한 욕설이 "땡큐땡큐"처럼 두 번 바뀌거나 범위가 서로 꼬인다.
     */
    private static List<int[]> merge(List<int[]> spans) {
        spans.sort((a, b) -> Integer.compare(a[0], b[0]));
        List<int[]> regions = new ArrayList<>();
        for (int[] span : spans) {
            int[] last = regions.isEmpty() ? null : regions.get(regions.size() - 1);
            if (last != null && span[0] <= last[1] + 1) {
                last[1] = Math.max(last[1], span[1]);
            } else {
                regions.add(new int[]{span[0], span[1]});
            }
        }
        return regions;
    }

    /** 사전에 =단어 로 적은 단어: 앞뒤가 다른 글자에 붙어 있으면 걸지 않는다 */
    private static final int WHOLE_WORD = 1;

    private static void add(String word, PatternMatcher korean, PatternMatcher latin) {
        if (word == null || word.isBlank()) return;
        String w = word.strip();
        int flags = 0;
        if (w.startsWith("=")) {
            flags = WHOLE_WORD;
            w = w.substring(1).strip();
            if (w.isEmpty()) return;
        }
        if (TextNormalizer.containsHangul(w)) {
            korean.add(TextNormalizer.korean(w).codes(), flags);
        } else {
            latin.add(TextNormalizer.latin(w).codes(), flags);
        }
    }

    /** 바꿀 원문 범위 {시작, 끝(포함)}를 spans에 모은다 */
    private static void collect(String text, TextNormalizer.Normalized n,
                                PatternMatcher banned, PatternMatcher allowed, List<int[]> spans) {
        if (banned.isEmpty() || n.length() == 0) return;
        List<int[]> hits = banned.findAll(n.codes());
        if (hits.isEmpty()) return;
        List<int[]> allows = allowed.findAll(n.codes());

        for (int[] hit : hits) {
            if (insideAllowed(hit, allows)) continue;
            int from = n.start()[hit[0]];
            int to = n.end()[hit[1]];
            if (endsInsideSyllable(n, hit[1])) continue;
            if ((hit[2] & WHOLE_WORD) != 0 && !isWholeWord(text, from, to)) continue;
            if (!acceptAcrossSpaces(text, from, to)) continue;
            spans.add(new int[]{from, to});
        }
    }

    /** 걸린 금칙어가 허용어 범위 안에 통째로 들어 있나 */
    private static boolean insideAllowed(int[] hit, List<int[]> allows) {
        for (int[] a : allows) {
            if (a[0] <= hit[0] && hit[1] <= a[1]) return true;
        }
        return false;
    }

    /**
     * 걸린 마지막 토큰 뒤에 같은 글자의 토큰(받침)이 더 있으면 글자 중간에서 끊긴 것이다.
     * ("시바"가 "시방"의 ㅅㅣㅂㅏ까지만 걸린 경우. "시발"처럼 받침까지 사전에 있으면 그 단어로 걸린다)
     */
    private static boolean endsInsideSyllable(TextNormalizer.Normalized n, int lastToken) {
        int next = lastToken + 1;
        return next < n.length() && n.start()[next] == n.end()[lastToken];
    }

    /** 앞뒤가 글자(한글·영문)에 붙어 있지 않은가 */
    private static boolean isWholeWord(String text, int from, int to) {
        return (from == 0 || !Character.isLetter(text.charAt(from - 1)))
                && (to == text.length() - 1 || !Character.isLetter(text.charAt(to + 1)));
    }

    /**
     * 띄어쓰기를 넘어 걸린 경우, 앞뒤가 글자에 붙어 있으면 서로 다른 낱말이 우연히 이어진 것으로 본다.
     * ("한시 발표" → '시'가 '한'에 붙어 있으므로 아님, "시 발" → 앞뒤가 비었으므로 욕설)
     */
    private static boolean acceptAcrossSpaces(String text, int from, int to) {
        boolean crossesSpace = false;
        for (int i = from; i <= to; i++) {
            if (Character.isWhitespace(text.charAt(i))) {
                crossesSpace = true;
                break;
            }
        }
        return !crossesSpace || isWholeWord(text, from, to);
    }
}
