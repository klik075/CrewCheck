package com.WhoisntCitizen_server.chat.filter;

import org.junit.jupiter.api.Test;
import org.junit.jupiter.params.ParameterizedTest;
import org.junit.jupiter.params.provider.CsvSource;

import static org.assertj.core.api.Assertions.assertThat;

/** 글자 수별 순화 단어 표 */
class ProfanityReplacementTest {

    @ParameterizedTest
    @CsvSource({
            "1, 땡큐",
            "2, 땡큐",
            "3, 좋아요",
            "4, 좋은친구",
            "5, 감사합니다",
            "6, 사이좋은친구",
            "10, 사이좋은친구"
    })
    void 글자_수에_맞는_단어를_고른다(int length, String expected) {
        assertThat(ProfanityReplacement.forLength(length)).isEqualTo(expected);
    }

    @Test
    void 공백은_세지_않는다() {
        assertThat(ProfanityReplacement.replace("시 발")).isEqualTo("땡큐");
        assertThat(ProfanityReplacement.replace("f u c k")).isEqualTo("좋은친구");
    }

    @Test
    void 끼워_넣은_숫자와_특수문자는_센다() {
        assertThat(ProfanityReplacement.replace("시1발")).isEqualTo("좋아요");
        assertThat(ProfanityReplacement.replace("시~!@발")).isEqualTo("감사합니다");
    }

    @Test
    void 이모지는_한_글자로_센다() {
        assertThat(ProfanityReplacement.replace("시😀발")).isEqualTo("좋아요"); // 시😀발
    }
}
