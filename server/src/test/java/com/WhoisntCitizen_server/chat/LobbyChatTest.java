package com.WhoisntCitizen_server.chat;

import com.WhoisntCitizen_server.chat.dto.ChatMessageResponse;
import com.WhoisntCitizen_server.chat.dto.ChatMessageSummary;
import com.WhoisntCitizen_server.chat.filter.ProfanityFilter;
import com.WhoisntCitizen_server.chat.service.ChatMessageService;
import com.WhoisntCitizen_server.chat.spam.ChatMutedException;
import com.WhoisntCitizen_server.chat.spam.ChatSpamGuard;
import com.WhoisntCitizen_server.chat.spam.ChatSpamPolicy;
import com.WhoisntCitizen_server.chat.spam.InMemoryChatSpamStore;
import com.WhoisntCitizen_server.common.exception.NotFoundException;
import com.WhoisntCitizen_server.game.lock.LocalGameLock;
import com.WhoisntCitizen_server.game.repository.InMemoryGameRepository;
import com.WhoisntCitizen_server.lobby.domain.room.Room;
import com.WhoisntCitizen_server.lobby.domain.room.RoomPlayer;
import com.WhoisntCitizen_server.user.entity.User;
import com.WhoisntCitizen_server.user.repository.UserRepository;
import org.junit.jupiter.api.BeforeEach;
import org.junit.jupiter.api.Test;

import java.util.List;
import java.util.Optional;

import static org.assertj.core.api.Assertions.assertThat;
import static org.assertj.core.api.Assertions.assertThatThrownBy;
import static org.mockito.Mockito.mock;
import static org.mockito.Mockito.when;

/** 로비 채팅: 방 참가와 관계없이 로그인 사용자끼리 대화하고, 방 채팅과 섞이지 않는다 */
class LobbyChatTest {

    static final long ROOM_ID = 1L;
    static final long CHULSOO_MEMBER = 10L, CHULSOO_USER = 1L; // 1번 방 참가자
    static final long YOUNGHEE_MEMBER = 20L, YOUNGHEE_USER = 2L; // 어느 방에도 없음 (로비)
    static final long NO_PROFILE_MEMBER = 30L;

    InMemoryChatMessageRepository messages;
    ChatMessageService service;

    @BeforeEach
    void setUp() {
        InMemoryLobbyRoomRepository rooms = new InMemoryLobbyRoomRepository();
        Room room = new Room(ROOM_ID, "마피아 1번방", CHULSOO_USER, 8);
        room.addPlayer(new RoomPlayer(CHULSOO_USER, "철수", false));
        rooms.save(room);

        UserRepository users = mock(UserRepository.class);
        when(users.findByMemberId(CHULSOO_MEMBER))
                .thenReturn(Optional.of(User.builder().id(CHULSOO_USER).nickname("철수").build()));
        when(users.findByMemberId(YOUNGHEE_MEMBER))
                .thenReturn(Optional.of(User.builder().id(YOUNGHEE_USER).nickname("영희").build()));
        when(users.findByMemberId(NO_PROFILE_MEMBER)).thenReturn(Optional.empty());

        messages = new InMemoryChatMessageRepository();
        service = new ChatMessageService(messages, rooms, users, new InMemoryGameRepository(), new LocalGameLock(),
                event -> { }, ProfanityFilter.of(List.of("시발"), List.of()),
                ChatSpamGuard.of(new InMemoryChatSpamStore(), ChatSpamPolicy.DEFAULT));
    }

    @Test
    void 방에_없어도_로비_채팅을_보내고_프로필_닉네임으로_저장된다() {
        ChatMessageResponse response = service.sendLobby(YOUNGHEE_MEMBER, "안녕하세요");

        assertThat(response.nickname()).isEqualTo("영희");
        assertThat(response.userId()).isEqualTo(YOUNGHEE_USER);
        assertThat(service.getLobbyMessages(null, null)).extracting(ChatMessageSummary::message)
                .containsExactly("안녕하세요");
    }

    @Test
    void afterId_이후의_메시지만_받는다() {
        service.sendLobby(YOUNGHEE_MEMBER, "입장 전 대화");
        long entryId = service.getLobbyMessages(null, 1).get(0).messageId(); // 로비에 들어올 때 받아 두는 마지막 id

        service.sendLobby(CHULSOO_MEMBER, "입장 후 대화");

        assertThat(service.getLobbyMessages(entryId, null)).extracting(ChatMessageSummary::message)
                .containsExactly("입장 후 대화");
    }

    @Test
    void 로비_채팅과_방_채팅은_섞이지_않는다() {
        service.sendLobby(YOUNGHEE_MEMBER, "로비에서");
        service.send(ROOM_ID, CHULSOO_MEMBER, "방에서");

        assertThat(service.getLobbyMessages(null, null)).extracting(ChatMessageSummary::message)
                .containsExactly("로비에서");
        assertThat(service.getMessages(ROOM_ID, CHULSOO_MEMBER, null, null)).extracting(ChatMessageSummary::message)
                .containsExactly("방에서");
        // 로비 저장소(0번)는 방 API로 접근할 수 없다
        assertThatThrownBy(() -> service.getMessages(ChatMessageService.LOBBY_ROOM_ID, CHULSOO_MEMBER, null, null))
                .isInstanceOf(NotFoundException.class);
    }

    @Test
    void 욕설_검열과_도배_방지가_똑같이_적용된다() {
        assertThat(service.sendLobby(YOUNGHEE_MEMBER, "아 시발").message()).isEqualTo("아 땡큐");

        service.sendLobby(CHULSOO_MEMBER, "도배");
        service.sendLobby(CHULSOO_MEMBER, "도배");
        assertThat(service.sendLobby(CHULSOO_MEMBER, "도배").spamWarning()).endsWith("(3/10)");
        for (int i = 4; i <= 9; i++) service.sendLobby(CHULSOO_MEMBER, "도배");
        assertThatThrownBy(() -> service.sendLobby(CHULSOO_MEMBER, "도배")).isInstanceOf(ChatMutedException.class);
    }

    @Test
    void 프로필이_없으면_400() {
        assertThatThrownBy(() -> service.sendLobby(NO_PROFILE_MEMBER, "안녕"))
                .isInstanceOf(IllegalArgumentException.class);
    }
}
