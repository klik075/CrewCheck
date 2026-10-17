package com.WhoisntCitizen_server.realtime.redis;

import org.slf4j.Logger;
import org.slf4j.LoggerFactory;
import org.springframework.data.redis.connection.Message;
import org.springframework.data.redis.connection.MessageListener;

import java.nio.charset.StandardCharsets;
import java.util.Objects;

/**
 * 실시간 신호 채널을 구독하다가 신호가 오면 이 서버의 연결에 반영한다. (5단계)
 * RedisMessageListenerContainer(RealtimeRedisConfig)가 채널 메시지마다 onMessage를 부른다.
 *
 *  - 자기가 보낸 신호(origin == 이 서버 ID)는 버린다. 보낼 때 이미 자기 연결에 직접 보냈다.
 *  - 읽을 수 없는 신호(깨진 JSON, 모르는 op, 다른 버전)는 경고만 남기고 버린다.
 *    서버를 한 대씩 교체하는 동안 새 서버가 보낸 신호를 옛 서버가 모를 수 있는데, 그래도 구독이 멈추면 안 된다.
 */
public class RealtimeSignalListener implements MessageListener {

    private static final Logger log = LoggerFactory.getLogger(RealtimeSignalListener.class);

    private final RedisRealtimeNotifier notifier;
    private final RealtimeSignalCodec codec;

    public RealtimeSignalListener(RedisRealtimeNotifier notifier, RealtimeSignalCodec codec) {
        this.notifier = Objects.requireNonNull(notifier, "notifier");
        this.codec = Objects.requireNonNull(codec, "codec");
    }

    @Override
    public void onMessage(Message message, byte[] pattern) {
        String body = new String(message.getBody(), StandardCharsets.UTF_8);
        RealtimeSignal signal;
        try {
            signal = codec.decode(body);
        } catch (RuntimeException e) {
            log.warn("[ws] 읽을 수 없는 실시간 신호를 버림: {}", e.getMessage());
            return;
        }
        if (notifier.serverId().equals(signal.origin())) {
            return;
        }
        try {
            notifier.deliver(signal);
        } catch (RuntimeException e) {
            log.warn("[ws] 실시간 신호 반영 실패: op={}, from={}, {}", signal.op(), signal.origin(), e.toString());
        }
    }
}
