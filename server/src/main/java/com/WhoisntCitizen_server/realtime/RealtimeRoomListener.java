package com.WhoisntCitizen_server.realtime;

import com.WhoisntCitizen_server.game.event.PlayersDepartedEvent;
import com.WhoisntCitizen_server.lobby.event.RoomPlayerKickedEvent;
import com.WhoisntCitizen_server.lobby.event.RoomPlayerLeftEvent;
import com.WhoisntCitizen_server.lobby.event.RoomStateChangedEvent;
import com.WhoisntCitizen_server.realtime.protocol.ServerMessage;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;
import org.springframework.context.event.EventListener;

import java.util.Objects;

/**
 * 방 이벤트를 실시간 신호로 바꾼다.
 *
 *  - RoomStateChangedEvent (방 저장·삭제) → 그 방 구독자에게 room.changed
 *      삭제된 방이면 보낸 뒤 그 방의 구독을 모두 푼다. (클라이언트는 GET에서 방이 없음을 보고 로비로 간다)
 *  - 퇴장(RoomPlayerLeftEvent)·강퇴(RoomPlayerKickedEvent)·게임 중 연결 끊김(PlayersDepartedEvent)
 *      → 그 사람의 연결을 방 구독에서 뺀다. 구독할 때만 참가자인지 확인하므로, 빠진 뒤에도 신호를 받지 않게 여기서 정리한다.
 *
 * 순서: 방이 저장되면(room.changed) 나간 사람에게도 한 번 신호가 간다. 그 사람은 GET으로 자기가 빠졌음(강퇴 등)을 알 수 있다.
 * 그 뒤 퇴장·강퇴 이벤트로 구독이 풀린다.
 */
public class RealtimeRoomListener {

    private static final Logger log = LoggerFactory.getLogger(RealtimeRoomListener.class);

    private final RealtimeNotifier notifier;

    public RealtimeRoomListener(RealtimeNotifier notifier) {
        this.notifier = Objects.requireNonNull(notifier, "notifier");
    }

    @EventListener
    public void onRoomStateChanged(RoomStateChangedEvent event) {
        if (event.roomId() == null) {
            return;
        }
        notifier.sendToRoom(event.roomId(), new ServerMessage.RoomChanged(event.roomId()));
        if (event.deleted()) {
            notifier.removeRoom(event.roomId());
        }
    }

    @EventListener
    public void onPlayerLeft(RoomPlayerLeftEvent event) {
        removeUser(event.roomId(), event.userId());
    }

    @EventListener
    public void onPlayerKicked(RoomPlayerKickedEvent event) {
        removeUser(event.roomId(), event.userId());
    }

    @EventListener
    public void onPlayersDeparted(PlayersDepartedEvent event) {
        Long roomId = parseRoomId(event.roomId());
        if (roomId == null || event.userIds() == null) {
            return;
        }
        event.userIds().forEach(userId -> removeUser(roomId, userId));
    }

    private void removeUser(Long roomId, Long userId) {
        if (roomId == null || userId == null) {
            return;
        }
        notifier.removeUserFromRoom(roomId, userId);
    }

    private static Long parseRoomId(String roomId) {
        if (roomId == null) {
            return null;
        }
        try {
            return Long.valueOf(roomId);
        } catch (NumberFormatException e) {
            log.debug("[ws] 로비 방이 아닌 게임이라 구독 정리를 건너뜀: roomId={}", roomId);
            return null;
        }
    }
}
