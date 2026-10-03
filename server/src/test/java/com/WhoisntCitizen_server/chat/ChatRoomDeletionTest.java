package com.WhoisntCitizen_server.chat;

import com.WhoisntCitizen_server.chat.entity.MessageType;
import com.WhoisntCitizen_server.chat.service.ChatLobbyEventListener;
import com.WhoisntCitizen_server.chat.service.ChatMessageService;
import com.WhoisntCitizen_server.game.repository.InMemoryGameRepository;
import com.WhoisntCitizen_server.lobby.event.RoomDeletedEvent;
import com.WhoisntCitizen_server.member.repository.UserRepository;
import org.junit.jupiter.api.Test;

import static org.assertj.core.api.Assertions.assertThat;
import static org.mockito.Mockito.mock;

/** 방이 삭제되면 그 방의 채팅도 지운다. (방 id는 다시 쓰이지 않으므로 남겨 두면 계속 쌓인다) */
class ChatRoomDeletionTest {

    @Test
    void 방이_삭제되면_그_방의_메시지만_지운다() {
        InMemoryChatMessageRepository messages = new InMemoryChatMessageRepository();
        ChatMessageService service = new ChatMessageService(messages, new InMemoryLobbyRoomRepository(),
                mock(UserRepository.class), new InMemoryGameRepository());
        ChatLobbyEventListener listener = new ChatLobbyEventListener(service);
        messages.save(1L, MessageType.SYSTEM, 0L, "시스템", "방 1 안내");
        messages.save(2L, MessageType.SYSTEM, 0L, "시스템", "방 2 안내");

        listener.onRoomDeleted(new RoomDeletedEvent(1L));

        assertThat(messages.findLatest(1L, 10)).isEmpty();
        assertThat(messages.findLatest(2L, 10)).hasSize(1);
    }
}
