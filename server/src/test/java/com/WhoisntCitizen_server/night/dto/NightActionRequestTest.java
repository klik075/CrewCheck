package com.WhoisntCitizen_server.night.dto;

import com.WhoisntCitizen_server.jobs.domain.ActionCode;
import org.junit.jupiter.api.Test;

import static org.assertj.core.api.Assertions.assertThat;
import static org.assertj.core.api.Assertions.assertThatThrownBy;

/** 밤 행동 요청의 actionCode: 비어 있으면 기본 능력, JsonUtility가 보내는 ""도 비어 있는 것으로 본다. */
class NightActionRequestTest {

    @Test
    void 비어_있으면_기본_능력이다() {
        assertThat(new NightActionRequest(3L).actionCodeOrNull()).isNull();
        assertThat(new NightActionRequest(3L, "").actionCodeOrNull()).isNull();
        assertThat(new NightActionRequest(3L, "  ").actionCodeOrNull()).isNull();
    }

    @Test
    void 능력_이름이면_그_능력이다() {
        assertThat(new NightActionRequest(0L, "KRAKEN_STRIKE").actionCodeOrNull()).isEqualTo(ActionCode.KRAKEN_STRIKE);
    }

    @Test
    void 없는_이름이면_400이다() {
        assertThatThrownBy(() -> new NightActionRequest(3L, "FLY").actionCodeOrNull())
                .isInstanceOf(IllegalArgumentException.class)
                .hasMessageContaining("없는 능력");
    }
}
