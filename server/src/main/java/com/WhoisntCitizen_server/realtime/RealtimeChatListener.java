package com.WhoisntCitizen_server.realtime;

import com.WhoisntCitizen_server.chat.event.ChatMessageSavedEvent;
import com.WhoisntCitizen_server.realtime.protocol.ServerMessage;
import org.springframework.context.event.EventListener;

import java.util.Objects;

/**
 * 채팅 메시지가 저장되면(ChatMessageSavedEvent) chat.changed를 보낸다.
 *  - 모두에게 보이는 메시지(일반 채팅, 시스템 안내) → 방 구독자 전원
 *  - 일부에게만 보이는 메시지(사망자 채팅, 밤의 해적·세이렌 채팅, 해적 안내) → 볼 수 있는 사람의 연결에만
 * 볼 수 없는 사람은 신호 자체를 받지 않으므로 "지금 숨은 대화가 오간다"는 것도 알 수 없다.
 */
public class RealtimeChatListener {

    private final RealtimeNotifier notifier;

    public RealtimeChatListener(RealtimeNotifier notifier) {
        this.notifier = Objects.requireNonNull(notifier, "notifier");
    }

    @EventListener
    public void onChatMessageSaved(ChatMessageSavedEvent event) {
        ServerMessage message = new ServerMessage.ChatChanged(event.roomId(), event.messageId());
        if (event.visibleToEveryone()) {
            notifier.sendToRoom(event.roomId(), message);
        } else {
            notifier.sendToUsersInRoom(event.roomId(), event.audience(), message);
        }
    }
}
