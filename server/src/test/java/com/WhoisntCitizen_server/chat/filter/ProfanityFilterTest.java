package com.WhoisntCitizen_server.chat.filter;

import org.junit.jupiter.api.BeforeAll;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.params.ParameterizedTest;
import org.junit.jupiter.params.provider.ValueSource;

import java.io.IOException;
import java.io.InputStream;
import java.util.List;

import static org.assertj.core.api.Assertions.assertThat;

/** 욕설 검열: 실제 사전(resources/profanity)으로 우회 표현과 오탐을 확인한다 */
class ProfanityFilterTest {

    static ProfanityFilter filter;

    @BeforeAll
    static void loadDictionary() throws IOException {
        filter = ProfanityFilter.of(read("/profanity/banned-words.txt"), read("/profanity/allowed-words.txt"));
    }

    private static List<String> read(String path) throws IOException {
        try (InputStream in = ProfanityFilterTest.class.getResourceAsStream(path)) {
            assertThat(in).as(path).isNotNull();
            return ProfanityFilter.readWords(in);
        }
    }

    // ---------- 바꾸기 (글자 수별 순화 단어) ----------

    @Test
    void 걸린_부분을_글자_수에_맞는_순화_단어로_바꾼다() {
        assertThat(filter.mask("좆")).isEqualTo("땡큐");                 // 1글자
        assertThat(filter.mask("아 시발 진짜")).isEqualTo("아 땡큐 진짜");   // 2글자
        assertThat(filter.mask("2번 개새끼 마피아임")).isEqualTo("2번 좋아요 마피아임"); // 3글자
        assertThat(filter.mask("호로새끼")).isEqualTo("좋은친구");         // 4글자
        assertThat(filter.mask("tlqkf")).isEqualTo("감사합니다");          // 5글자
        assertThat(filter.mask("시발병신개새끼")).isEqualTo("사이좋은친구"); // 7글자 (맞닿은 욕설은 한 덩어리)
    }

    @Test
    void 맞닿은_욕설은_합쳐서_한_번만_바꾸고_떨어진_욕설은_따로_바꾼다() {
        assertThat(filter.mask("시발병신")).isEqualTo("좋은친구");
        assertThat(filter.mask("시발 병신")).isEqualTo("땡큐 땡큐");
    }

    @Test
    void 겹쳐_걸린_금칙어도_한_번만_바꾼다() {
        // "시발"은 사전의 '시바'와 '시발'에 함께 걸리지만 한 곳이다
        ProfanityFilter.Result result = filter.filter("시발");
        assertThat(result.text()).isEqualTo("땡큐");
        assertThat(result.matchCount()).isEqualTo(1);
    }

    @Test
    void 걸린_게_없으면_원문을_그대로_돌려준다() {
        String text = "2번이 마피아 같은데? 3번은 선원 같아";
        ProfanityFilter.Result result = filter.filter(text);
        assertThat(result.text()).isSameAs(text);
        assertThat(result.detected()).isFalse();
    }

    @Test
    void 여러_개가_걸리면_모두_바꾼다() {
        ProfanityFilter.Result result = filter.filter("시발 병신아 fuck");
        assertThat(result.text()).isEqualTo("땡큐 땡큐아 좋은친구");
        assertThat(result.matchCount()).isEqualTo(3);
    }

    // ---------- 우회 표현 ----------

    @ParameterizedTest
    @ValueSource(strings = {
            "시발", "씨발", "씨빨", "씨팔",
            "시 발", "시.발", "시1발", "시_발", "시~!@발",   // 띄어쓰기·특수문자·숫자 끼워 넣기
            "씨이이발", "씨발발발", "시바알",               // 늘여 쓰기·반복
            "tlqkf", "Tlqkf", "TLQKF",                     // 영문 자판
            "ㅅㅂ", "ㅆㅂ", "ㅅ ㅂ", "ㅅㅅㅂㅂ", "ㅄ", "ㅂㅅ", // 초성
            "ㅅㅣㅂㅏㄹ",                                    // 자모를 하나씩 친 경우
            "병신", "qudtls", "지랄", "존나", "좆",
            "fuck", "FUCK", "f u c k", "fuuuuck", "sh!t", "sh1t", "a$$hole", "b1tch"
    })
    void 우회_표현도_잡는다(String text) {
        assertThat(filter.containsProfanity(text)).as(text).isTrue();
        assertThat(filter.mask(text)).as(text).doesNotContain("시발", "씨발", "병신", "fuck");
    }

    @Test
    void 우회_표현은_원문에서_걸린_글자_수로_바꾼다() {
        // 끼워 넣은 숫자·특수문자도 글자 수에 들어가고, 공백은 세지 않는다
        assertThat(filter.mask("야 시1발 뭐함")).isEqualTo("야 좋아요 뭐함");
        assertThat(filter.mask("tlqkf 진짜")).isEqualTo("감사합니다 진짜");
        assertThat(filter.mask("씨이이발")).isEqualTo("좋은친구");
        assertThat(filter.mask("ㅅㅅㅂㅂ")).isEqualTo("좋은친구");
        assertThat(filter.mask("시 발")).isEqualTo("땡큐");
        assertThat(filter.mask("f u c k")).isEqualTo("좋은친구");
    }

    @Test
    void 금칙어_앞뒤로_다른_말이_붙어도_잡는다() {
        assertThat(filter.mask("시발놈아")).isEqualTo("땡큐놈아");
        assertThat(filter.mask("에이씨발")).isEqualTo("에이땡큐");
    }

    // ---------- 오탐 방지 ----------

    @ParameterizedTest
    @ValueSource(strings = {
            "여기가 시발점이야", "시바견 귀엽다", "병신년 새해", "등신대 세워 놨어",  // 허용어
            "오후 3시 발표합니다", "한시 발견", "수박씨 발라 먹어",                 // 띄어쓰기로 나뉜 다른 낱말
            "옷보다 신발", "있습니다", "웃방", "갔습니다",                          // 종성 ㅅ + 초성 ㅂ ≠ ㅅㅂ
            "news", "whats new", "shows", "lowsodium",                             // 영문 자판으로 ㅈㄴ 등이 되지 않게
            "보지 마", "자지 마", "능력 발동", "class", "assume",
            "시방 뭐하는겨", "씨받이", "시발택시",                                  // 받침 앞에서 끊긴 경우·허용어
            "scunthorpe", "snigger"                                                // =단어: 낱말 전체일 때만
    })
    void 정상_문장은_바꾸지_않는다(String text) {
        assertThat(filter.mask(text)).as(text).isEqualTo(text);
    }

    @Test
    void 낱말_전체만_거는_단어는_따로_쓰면_바꾼다() {
        assertThat(filter.mask("you cunt")).isEqualTo("you 좋은친구");
        ProfanityFilter custom = ProfanityFilter.of(List.of("=바보"), List.of());
        assertThat(custom.mask("이 바보")).isEqualTo("이 땡큐");
        assertThat(custom.mask("바보야")).isEqualTo("바보야");
    }

    @Test
    void 허용어_옆의_진짜_욕설은_바꾼다() {
        assertThat(filter.mask("시발점 시발")).isEqualTo("시발점 땡큐");
    }

    // ---------- 사전·설정 ----------

    @Test
    void 사전_파일은_주석과_빈_줄을_무시한다() throws IOException {
        InputStream in = new java.io.ByteArrayInputStream(
                "﻿# 주석\n\n시발  # 줄 끝 주석\n  병신 \n".getBytes(java.nio.charset.StandardCharsets.UTF_8));
        assertThat(ProfanityFilter.readWords(in)).containsExactly("시발", "병신");
    }

    @Test
    void 꺼진_필터는_아무것도_바꾸지_않는다() {
        assertThat(ProfanityFilter.disabled().isEnabled()).isFalse();
        assertThat(ProfanityFilter.disabled().mask("시발")).isEqualTo("시발");
    }

    @Test
    void 빈_문자열과_null을_그대로_돌려준다() {
        assertThat(filter.mask("")).isEmpty();
        assertThat(filter.mask(null)).isNull();
    }

    @Test
    void 직접_만든_사전으로도_동작한다() {
        ProfanityFilter custom = ProfanityFilter.of(List.of("바보"), List.of("바보온달"));
        assertThat(custom.mask("이 바보야")).isEqualTo("이 땡큐야");
        assertThat(custom.mask("바보온달 이야기")).isEqualTo("바보온달 이야기");
    }
}
