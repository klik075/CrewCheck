package com.WhoisntCitizen_server.chat;

import com.WhoisntCitizen_server.chat.controller.ChatMessageController;
import com.WhoisntCitizen_server.chat.entity.ChatMessage;
import com.WhoisntCitizen_server.chat.filter.ProfanityFilter;
import com.WhoisntCitizen_server.chat.service.ChatMessageService;
import com.WhoisntCitizen_server.chat.spam.ChatSpamGuard;
import com.WhoisntCitizen_server.chat.spam.ChatSpamPolicy;
import com.WhoisntCitizen_server.chat.spam.InMemoryChatSpamStore;
import com.WhoisntCitizen_server.common.exception.GlobalExceptionHandler;
import com.WhoisntCitizen_server.game.lock.LocalGameLock;
import com.WhoisntCitizen_server.game.repository.InMemoryGameRepository;
import com.WhoisntCitizen_server.lobby.domain.room.Room;
import com.WhoisntCitizen_server.lobby.domain.room.RoomPlayer;
import com.WhoisntCitizen_server.user.entity.User;
import com.WhoisntCitizen_server.user.repository.UserRepository;
import org.junit.jupiter.api.AfterEach;
import org.junit.jupiter.api.BeforeEach;
import org.junit.jupiter.api.Test;
import org.springframework.http.MediaType;
import org.springframework.security.core.context.SecurityContextHolder;
import org.springframework.security.oauth2.jwt.Jwt;
import org.springframework.security.oauth2.server.resource.authentication.JwtAuthenticationToken;
import org.springframework.security.web.method.annotation.AuthenticationPrincipalArgumentResolver;
import org.springframework.test.web.servlet.MockMvc;
import org.springframework.test.web.servlet.ResultActions;
import org.springframework.test.web.servlet.setup.MockMvcBuilders;

import java.util.List;
import java.util.Optional;

import static org.assertj.core.api.Assertions.assertThat;
import static org.mockito.Mockito.mock;
import static org.mockito.Mockito.when;
import static org.springframework.test.web.servlet.request.MockMvcRequestBuilders.post;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.header;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.jsonPath;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.status;

/** 채팅 도배 방지 API: 3번째부터 응답에 경고, 10번째는 저장하지 않고 429 + 3분 채팅 금지 */
class ChatSpamTest {

    static final long ROOM_ID = 1L;
    static final long CHULSOO_MEMBER = 10L, CHULSOO_USER = 1L;
    static final long MINSU_MEMBER = 40L, MINSU_USER = 3L;

    MockMvc mvc;
    InMemoryChatMessageRepository messages;

    @BeforeEach
    void setUp() {
        InMemoryLobbyRoomRepository rooms = new InMemoryLobbyRoomRepository();
        Room room = new Room(ROOM_ID, "마피아 1번방", CHULSOO_USER, 8);
        room.addPlayer(new RoomPlayer(CHULSOO_USER, "철수", false));
        room.addPlayer(new RoomPlayer(MINSU_USER, "민수", false));
        rooms.save(room);

        UserRepository users = mock(UserRepository.class);
        when(users.findByMemberId(CHULSOO_MEMBER))
                .thenReturn(Optional.of(User.builder().id(CHULSOO_USER).nickname("철수").build()));
        when(users.findByMemberId(MINSU_MEMBER))
                .thenReturn(Optional.of(User.builder().id(MINSU_USER).nickname("민수").build()));

        messages = new InMemoryChatMessageRepository();
        ChatSpamGuard guard = ChatSpamGuard.of(new InMemoryChatSpamStore(), ChatSpamPolicy.DEFAULT);
        ChatMessageService service = new ChatMessageService(messages, rooms, users, new InMemoryGameRepository(),
                new LocalGameLock(), event -> { }, ProfanityFilter.disabled(), guard);

        mvc = MockMvcBuilders.standaloneSetup(new ChatMessageController(service, ""))
                .setCustomArgumentResolvers(new AuthenticationPrincipalArgumentResolver())
                .setControllerAdvice(new GlobalExceptionHandler())
                .build();
        loginAs(CHULSOO_MEMBER);
    }

    @AfterEach
    void tearDown() {
        SecurityContextHolder.clearContext();
    }

    private static void loginAs(long memberId) {
        Jwt jwt = Jwt.withTokenValue("test-token").header("alg", "HS256").subject(String.valueOf(memberId)).build();
        SecurityContextHolder.getContext().setAuthentication(new JwtAuthenticationToken(jwt));
    }

    private ResultActions send(String message) throws Exception {
        return mvc.perform(post("/api/v1/rooms/{roomId}/messages", ROOM_ID)
                .contentType(MediaType.APPLICATION_JSON)
                .content("{\"message\":\"" + message + "\"}"));
    }

    private List<String> saved() {
        return messages.findLatest(ROOM_ID, 100).stream().map(ChatMessage::message).toList();
    }

    @Test
    void 두_번째까지는_경고가_없다() throws Exception {
        send("2번 마피아").andExpect(status().isCreated()).andExpect(jsonPath("$.spamWarning").doesNotExist());
        send("2번 마피아").andExpect(status().isCreated()).andExpect(jsonPath("$.spamWarning").doesNotExist());
    }

    @Test
    void 세_번째부터는_저장하면서_경고_문구를_보낸다() throws Exception {
        send("도배");
        send("도배");
        send("도배").andExpect(status().isCreated())
                .andExpect(jsonPath("$.message").value("도배"))
                .andExpect(jsonPath("$.spamWarning")
                        .value("같은 내용을 3번 연속으로 보냈습니다. 도배를 계속하면 10번째에 3분간 채팅이 금지됩니다. (3/10)"));
        send("도배").andExpect(status().isCreated())
                .andExpect(jsonPath("$.spamWarning")
                        .value("같은 내용을 4번 연속으로 보냈습니다. 도배를 계속하면 10번째에 3분간 채팅이 금지됩니다. (4/10)"));
        assertThat(saved()).hasSize(4);
    }

    @Test
    void 열_번째는_저장하지_않고_429와_함께_경고와_금지_안내를_보낸다() throws Exception {
        for (int i = 0; i < 9; i++) send("도배").andExpect(status().isCreated());

        send("도배").andExpect(status().isTooManyRequests())
                .andExpect(header().string("Retry-After", "180"))
                .andExpect(jsonPath("$.code").value("CHAT_MUTED"))
                .andExpect(jsonPath("$.warning").value("같은 내용을 10번 연속으로 보냈습니다. (10/10)"))
                .andExpect(jsonPath("$.message").value("도배로 3분간 채팅이 금지되었습니다."))
                .andExpect(jsonPath("$.remainingSeconds").value(180));
        assertThat(saved()).hasSize(9);
    }

    @Test
    void 금지_중에는_다른_내용도_429로_막는다() throws Exception {
        for (int i = 0; i < 10; i++) send("도배");

        send("이제 안 할게요").andExpect(status().isTooManyRequests())
                .andExpect(jsonPath("$.code").value("CHAT_MUTED"))
                .andExpect(jsonPath("$.warning").doesNotExist())
                .andExpect(jsonPath("$.message").value(org.hamcrest.Matchers.startsWith("도배로 채팅이 금지된 상태입니다.")));
        assertThat(saved()).doesNotContain("이제 안 할게요");

        // 다른 사람은 그대로 채팅할 수 있다
        loginAs(MINSU_MEMBER);
        send("철수 조용하네").andExpect(status().isCreated());
    }
}
