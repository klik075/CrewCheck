package com.WhoisntCitizen_server.contract;

import com.WhoisntCitizen_server.game.dto.DayExtendResponse;
import com.WhoisntCitizen_server.game.dto.GameStateResponse;
import com.WhoisntCitizen_server.game.entity.GamePhase;
import com.WhoisntCitizen_server.vote.dto.ExecutionResultResponse;
import com.WhoisntCitizen_server.vote.dto.ExecutionResultResponse.VoteCount;
import org.junit.jupiter.api.Test;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.boot.test.context.SpringBootTest;
import tools.jackson.databind.json.JsonMapper;

import java.time.Instant;
import java.util.List;
import java.util.Map;

import static org.assertj.core.api.Assertions.assertThat;

/**
 * 서버가 실제로 쓰는 JsonMapper로 직렬화해 Unity JsonUtility가 읽을 수 있는 형태인지 확인한다.
 * - 시각은 ISO-8601 문자열(UTC, ...Z)
 * - 득표는 Map이 아니라 객체 배열(votes)로도 내려간다
 */
@SpringBootTest
class GameApiJsonContractTest {

    @Autowired
    JsonMapper jsonMapper;

    @Test
    void 게임_상태의_시각은_ISO_문자열이고_serverTime이_포함된다() {
        GameStateResponse state = new GameStateResponse("g-1", GamePhase.NIGHT, 1,
                Instant.parse("2026-10-01T12:00:30Z"), Instant.parse("2026-10-01T12:00:00Z"), 3,
                List.of(new GameStateResponse.PlayerView(7L, "p7", true)), null, null);

        String json = jsonMapper.writeValueAsString(state);

        assertThat(json).contains("\"phaseEndsAt\":\"2026-10-01T12:00:30Z\"");
        assertThat(json).contains("\"serverTime\":\"2026-10-01T12:00:00Z\"");
        assertThat(json).contains("\"phase\":\"NIGHT\"");
    }

    @Test
    void 토론_연장_응답은_클라이언트_DTO와_같은_이름이고_마감은_ISO_문자열이다() {
        DayExtendResponse response = new DayExtendResponse(true, GamePhase.DAY, 3,
                Instant.parse("2026-10-01T12:01:10Z"), 10, 1);

        String json = jsonMapper.writeValueAsString(response);

        assertThat(json).contains("\"accepted\":true", "\"phase\":\"DAY\"", "\"phaseVersion\":3",
                "\"phaseEndsAt\":\"2026-10-01T12:01:10Z\"", "\"addedSeconds\":10", "\"extendedCount\":1");
    }

    @Test
    void 처형_결과의_votes는_객체_배열이다() {
        ExecutionResultResponse result = new ExecutionResultResponse(2, 5L, "p5", false,
                Map.of(5L, 3, 7L, 1), List.of(new VoteCount(5L, "p5", 3), new VoteCount(7L, "p7", 1)));

        String json = jsonMapper.writeValueAsString(result);

        assertThat(json).contains("\"votes\":[{\"playerId\":5,\"nickname\":\"p5\",\"count\":3},{\"playerId\":7,\"nickname\":\"p7\",\"count\":1}]");
    }
}