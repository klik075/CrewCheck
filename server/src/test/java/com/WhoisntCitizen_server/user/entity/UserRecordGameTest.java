package com.WhoisntCitizen_server.user.entity;

import com.WhoisntCitizen_server.jobs.domain.Faction;
import org.junit.jupiter.api.Test;

import static org.assertj.core.api.Assertions.assertThat;

class UserRecordGameTest {

    @Test
    void 전체_전적과_진영별_전적이_함께_오른다() {
        User user = User.builder().id(1L).nickname("철수").build();

        user.recordGame(Faction.CREW, true);
        user.recordGame(Faction.CREW, false);
        user.recordGame(Faction.PIRATE, true);
        user.recordGame(Faction.NEUTRAL, false);

        assertThat(user.getPlayCount()).isEqualTo(4);
        assertThat(user.getWinCount()).isEqualTo(2);
        assertThat(user.getCrewPlayCount()).isEqualTo(2);
        assertThat(user.getCrewWinCount()).isEqualTo(1);
        assertThat(user.getPiratePlayCount()).isEqualTo(1);
        assertThat(user.getPirateWinCount()).isEqualTo(1);
        assertThat(user.getNeutralPlayCount()).isEqualTo(1);
        assertThat(user.getNeutralWinCount()).isZero();
    }

    @Test
    void 진영을_모르면_전체_전적만_오른다() {
        User user = User.builder().id(1L).nickname("철수").build();

        user.recordGame(null, true);

        assertThat(user.getPlayCount()).isEqualTo(1);
        assertThat(user.getWinCount()).isEqualTo(1);
        assertThat(user.getCrewPlayCount()).isZero();
    }
}
