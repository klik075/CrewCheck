package com.WhoisntCitizen_server.realtime;

import com.WhoisntCitizen_server.game.event.GameStateChangedEvent;
import com.WhoisntCitizen_server.realtime.protocol.ServerMessage;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;
import org.springframework.context.event.EventListener;

import java.util.Objects;

/**
 * 게임 상태가 저장되면(GameStateChangedEvent) 그 방을 구독 중인 연결에 game.changed를 보낸다.
 * 이벤트는 게임 잠금이 풀린 뒤 같은 스레드에서 온다. 보내기는 연결마다 버퍼에 넣고 바로 돌아오므로 오래 걸리지 않는다.
 */
public class RealtimeGameListener {

    private static final Logger log = LoggerFactory.getLogger(RealtimeGameListener.class);

    private final RealtimeNotifier notifier;

    public RealtimeGameListener(RealtimeNotifier notifier) {
        this.notifier = Objects.requireNonNull(notifier, "notifier");
    }

    @EventListener
    public void onGameStateChanged(GameStateChangedEvent event) {
        Long roomId = parseRoomId(event.roomId());
        if (roomId == null) {
            log.debug("[ws] 로비 방이 아닌 게임이라 game.changed를 보내지 않음: gameId={}, roomId={}", event.gameId(), event.roomId());
            return;
        }
        notifier.sendToRoom(roomId, new ServerMessage.GameChanged(event.gameId(), event.phaseVersion()));
    }

    private static Long parseRoomId(String roomId) {
        if (roomId == null) {
            return null;
        }
        try {
            return Long.valueOf(roomId);
        } catch (NumberFormatException e) {
            return null;
        }
    }
}
