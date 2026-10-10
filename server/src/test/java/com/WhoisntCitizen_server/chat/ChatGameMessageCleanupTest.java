package com.WhoisntCitizen_server.chat;

import com.WhoisntCitizen_server.chat.entity.ChatMessage;
import com.WhoisntCitizen_server.chat.entity.MessageType;
import com.WhoisntCitizen_server.chat.service.ChatLobbyEventListener;
import com.WhoisntCitizen_server.chat.service.ChatMessageService;
import com.WhoisntCitizen_server.game.lock.LocalGameLock;
import com.WhoisntCitizen_server.game.repository.InMemoryGameRepository;
import com.WhoisntCitizen_server.lobby.event.RoomGameFinishedEvent;
import com.WhoisntCitizen_server.lobby.event.RoomGameStartingEvent;
import com.WhoisntCitizen_server.user.repository.UserRepository;
import org.junit.jupiter.api.BeforeEach;
import org.junit.jupiter.api.Test;

import static org.assertj.core.api.Assertions.assertThat;
import static org.mockito.Mockito.mock;

/** 게임이 끝나 대기실로 돌아오면 게임 중에 오간 메시지를 지운다. (게임 전 대기실 대화는 남긴다) */
class ChatGameMessageCleanupTest {

    private InMemoryChatMessageRepository messages;
    private ChatLobbyEventListener listener;

    @BeforeEach
    void setUp() {
        messages = new InMemoryChatMessageRepository();
        ChatMessageService service = new ChatMessageService(messages, new InMemoryLobbyRoomRepository(),
                mock(UserRepository.class), new InMemoryGameRepository(), new LocalGameLock());
        listener = new ChatLobbyEventListener(service);
    }

    private void say(long roomId, String text) {
        messages.save(roomId, MessageType.USER, 1L, "철수", text);
    }

    @Test
    void 대기실로_돌아오면_게임_중_메시지만_지운다() {
        say(1L, "대기실 대화");
        listener.onGameStarting(new RoomGameStartingEvent(1L));
        messages.save(1L, MessageType.SYSTEM, 0L, "SYSTEM", "1일차 밤이 되었습니다.");
        say(1L, "게임 중 대화");
        messages.save(1L, MessageType.DEAD, 1L, "철수", "사망자 채팅", "game-1");

        listener.onGameFinished(new RoomGameFinishedEvent(1L));
        messages.save(1L, MessageType.SYSTEM, 0L, "SYSTEM", "게임이 끝나 대기실로 돌아왔습니다.");

        assertThat(messages.findLatest(1L, 50)).extracting(ChatMessage::message)
                .containsExactly("대기실 대화", "게임이 끝나 대기실로 돌아왔습니다.");
    }

    @Test
    void 다른_방의_메시지는_건드리지_않는다() {
        listener.onGameStarting(new RoomGameStartingEvent(1L));
        say(1L, "방 1 게임 중");
        say(2L, "방 2 대화");

        listener.onGameFinished(new RoomGameFinishedEvent(1L));

        assertThat(messages.findLatest(1L, 50)).isEmpty();
        assertThat(messages.findLatest(2L, 50)).hasSize(1);
    }

    @Test
    void 시작_표시가_없으면_아무것도_지우지_않는다() {
        say(1L, "대화");

        listener.onGameFinished(new RoomGameFinishedEvent(1L));

        assertThat(messages.findLatest(1L, 50)).hasSize(1);
    }

    @Test
    void 다음_게임은_새_시작_표시부터_지운다() {
        listener.onGameStarting(new RoomGameStartingEvent(1L));
        say(1L, "1판");
        listener.onGameFinished(new RoomGameFinishedEvent(1L));
        say(1L, "1판 뒤 대기실");

        listener.onGameStarting(new RoomGameStartingEvent(1L));
        say(1L, "2판");
        listener.onGameFinished(new RoomGameFinishedEvent(1L));

        assertThat(messages.findLatest(1L, 50)).extracting(ChatMessage::message)
                .containsExactly("1판 뒤 대기실");
    }
}
