package com.WhoisntCitizen_server.realtime.redis;

import tools.jackson.core.JacksonException;
import tools.jackson.databind.json.JsonMapper;

/**
 * RealtimeSignal ↔ JSON 문자열. (Redis 채널에 실리는 모양)
 *
 * Spring의 JsonMapper를 주입받지 않고 직접 만든다. (RealtimeMessageCodec과 같은 이유)
 * spring.jackson.* 설정이 바뀌어도 서버끼리 주고받는 모양은 바뀌면 안 된다. 서버를 한 대씩 교체하는 동안 옛 서버와 새 서버가 섞인다.
 */
public class RealtimeSignalCodec {

    private final JsonMapper jsonMapper = JsonMapper.builder().build();

    public String encode(RealtimeSignal signal) {
        return jsonMapper.writeValueAsString(signal);
    }

    /**
     * @throws IllegalArgumentException 해석할 수 없을 때 (깨진 JSON, 모르는 op, 지원하지 않는 버전, 필수 칸 없음)
     */
    public RealtimeSignal decode(String json) {
        RealtimeSignal signal;
        try {
            signal = jsonMapper.readValue(json, RealtimeSignal.class);
        } catch (JacksonException e) {
            throw new IllegalArgumentException("실시간 신호를 읽을 수 없습니다: " + e.getOriginalMessage(), e);
        }
        if (signal == null || signal.op() == null || signal.origin() == null) {
            throw new IllegalArgumentException("실시간 신호에 op 또는 origin이 없습니다");
        }
        if (signal.v() != RealtimeSignal.VERSION) {
            throw new IllegalArgumentException("지원하지 않는 실시간 신호 버전입니다: " + signal.v());
        }
        return signal;
    }
}
