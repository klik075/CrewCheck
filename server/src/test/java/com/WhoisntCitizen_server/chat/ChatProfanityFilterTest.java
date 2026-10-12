package com.WhoisntCitizen_server.chat;

import com.WhoisntCitizen_server.chat.dto.ChatMessageResponse;
import com.WhoisntCitizen_server.chat.dto.ChatMessageSummary;
import com.WhoisntCitizen_server.chat.entity.ChatMessage;
import com.WhoisntCitizen_server.chat.filter.ProfanityFilter;
import com.WhoisntCitizen_server.chat.service.ChatMessageService;
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
import static org.mockito.Mockito.mock;
import static org.mockito.Mockito.when;

/** 채팅 전송 시 욕설 검열: 순화 단어로 바뀐 문장이 저장·응답·조회에 쓰이고, 원문은 어디에도 남지 않는다 */
class ChatProfanityFilterTest {

    static final long ROOM_ID = 1L;
    static final long CHULSOO_MEMBER = 10L, CHULSOO_USER = 1L;

    private InMemoryChatMessageRepository messages;
    private ChatMessageService service;

    @BeforeEach
    void setUp() {
        InMemoryLobbyRoomRepository rooms = new InMemoryLobbyRoomRepository();
        Room room = new Room(ROOM_ID, "마피아 1번방", CHULSOO_USER, 8);
        room.addPlayer(new RoomPlayer(CHULSOO_USER, "철수", false));
        rooms.save(room);

        UserRepository users = mock(UserRepository.class);
        when(users.findByMemberId(CHULSOO_MEMBER))
                .thenReturn(Optional.of(User.builder().id(CHULSOO_USER).nickname("철수").build()));

        messages = new InMemoryChatMessageRepository();
        ProfanityFilter filter = ProfanityFilter.of(List.of("시발", "병신", "fuck"), List.of("시발점"));
        service = new ChatMessageService(messages, rooms, users, new InMemoryGameRepository(), new LocalGameLock(),
                event -> { }, filter);
    }

    @Test
    void 욕설은_순화_단어로_바꿔서_저장하고_응답한다() {
        ChatMessageResponse response = service.send(ROOM_ID, CHULSOO_MEMBER, "아 시발 2번 병신이네");

        assertThat(response.message()).isEqualTo("아 땡큐 2번 땡큐이네");
        assertThat(messages.findLatest(ROOM_ID, 50)).extracting(ChatMessage::message)
                .containsExactly("아 땡큐 2번 땡큐이네");
    }

    @Test
    void 조회해도_바뀐_문장만_보인다() {
        service.send(ROOM_ID, CHULSOO_MEMBER, "tlqkf 진짜");

        List<ChatMessageSummary> list = service.getMessages(ROOM_ID, CHULSOO_MEMBER, null, null);
        assertThat(list).extracting(ChatMessageSummary::message).containsExactly("감사합니다 진짜");
    }

    @Test
    void 앞뒤_공백을_자른_뒤_검사한다() {
        assertThat(service.send(ROOM_ID, CHULSOO_MEMBER, "   fuck   ").message()).isEqualTo("좋은친구");
    }

    @Test
    void 정상_문장과_허용어는_그대로_둔다() {
        assertThat(service.send(ROOM_ID, CHULSOO_MEMBER, "여기가 시발점이야").message()).isEqualTo("여기가 시발점이야");
        assertThat(service.send(ROOM_ID, CHULSOO_MEMBER, "2번이 마피아 같은데?").message()).isEqualTo("2번이 마피아 같은데?");
    }

    @Test
    void 시스템_메시지는_검사하지_않는다() {
        // 서버가 만든 안내 문장이라 그대로 둔다 (닉네임 등 사용자 입력이 섞인 안내는 입력 단계에서 따로 막는다)
        assertThat(service.sendSystem(ROOM_ID, "시발 테스트").message()).isEqualTo("시발 테스트");
    }
}
