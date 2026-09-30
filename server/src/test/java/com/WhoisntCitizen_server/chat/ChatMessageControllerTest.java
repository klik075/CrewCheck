package com.WhoisntCitizen_server.chat;

import com.WhoisntCitizen_server.chat.controller.ChatMessageController;
import com.WhoisntCitizen_server.chat.service.ChatLobbyEventListener;
import com.WhoisntCitizen_server.chat.service.ChatMessageService;
import com.WhoisntCitizen_server.common.exception.GlobalExceptionHandler;
import com.WhoisntCitizen_server.lobby.domain.room.Room;
import com.WhoisntCitizen_server.lobby.domain.room.RoomPlayer;
import com.WhoisntCitizen_server.lobby.event.RoomPlayerJoinedEvent;
import com.WhoisntCitizen_server.lobby.event.RoomPlayerLeftEvent;
import com.WhoisntCitizen_server.member.entity.User;
import com.WhoisntCitizen_server.member.repository.UserRepository;
import com.jayway.jsonpath.JsonPath;
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

import java.nio.charset.StandardCharsets;
import java.util.Optional;

import static org.hamcrest.Matchers.hasSize;
import static org.mockito.Mockito.mock;
import static org.mockito.Mockito.when;
import static org.springframework.test.web.servlet.request.MockMvcRequestBuilders.get;
import static org.springframework.test.web.servlet.request.MockMvcRequestBuilders.post;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.jsonPath;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.status;

/**
 * 채팅 API 테스트 (Redis·DB 없이).
 *  - 로비 방 저장소는 InMemoryLobbyRoomRepository(메모리)로, 유저 프로필 저장소는 Mockito로 대신합니다.
 *  - 로그인 유저는 SecurityContext에 JWT(sub = memberId)를 넣어 흉내 냅니다.
 */
class ChatMessageControllerTest {

    static final long ROOM_ID = 1L;
    static final long CHULSOO_MEMBER = 10L, CHULSOO_USER = 1L;   // 로비 방 참가자
    static final long YOUNGHEE_MEMBER = 20L, YOUNGHEE_USER = 2L; // 로비 방 참가자 아님
    static final long NO_PROFILE_MEMBER = 30L;                   // 프로필(User) 없는 계정

    MockMvc mvc;
    ChatLobbyEventListener lobbyEvents;

    @BeforeEach
    void setUp() {
        InMemoryLobbyRoomRepository lobbyRooms = new InMemoryLobbyRoomRepository();
        Room room = new Room(ROOM_ID, "마피아 1번방", CHULSOO_USER, 8);
        room.addPlayer(new RoomPlayer(CHULSOO_USER, "철수", false));
        lobbyRooms.save(room);

        UserRepository users = mock(UserRepository.class);
        when(users.findByMemberId(CHULSOO_MEMBER))
                .thenReturn(Optional.of(User.builder().id(CHULSOO_USER).nickname("철수").build()));
        when(users.findByMemberId(YOUNGHEE_MEMBER))
                .thenReturn(Optional.of(User.builder().id(YOUNGHEE_USER).nickname("영희").build()));
        when(users.findByMemberId(NO_PROFILE_MEMBER)).thenReturn(Optional.empty());

        ChatMessageService messageService =
                new ChatMessageService(new InMemoryChatMessageRepository(), lobbyRooms, users);
        lobbyEvents = new ChatLobbyEventListener(messageService);

        mvc = MockMvcBuilders.standaloneSetup(new ChatMessageController(messageService, ""))
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
        Jwt jwt = Jwt.withTokenValue("test-token")
                .header("alg", "HS256")
                .subject(String.valueOf(memberId))
                .build();
        SecurityContextHolder.getContext().setAuthentication(new JwtAuthenticationToken(jwt));
    }

    private static long idOf(ResultActions r, String path) throws Exception {
        String body = r.andReturn().getResponse().getContentAsString(StandardCharsets.UTF_8);
        return ((Number) JsonPath.read(body, path)).longValue();
    }

    private ResultActions send(long room, String message) throws Exception {
        return mvc.perform(post("/api/v1/rooms/{roomId}/messages", room)
                .contentType(MediaType.APPLICATION_JSON)
                .content("{\"message\":\"" + message + "\"}"));
    }

    @Test
    void 전송_성공시_201과_명세_형식으로_응답하고_보낸_사람은_토큰과_로비_참가자_정보로_정한다() throws Exception {
        send(ROOM_ID, "2번이 마피아 같은데?")
                .andExpect(status().isCreated())
                .andExpect(jsonPath("$.messageId").isNumber())
                .andExpect(jsonPath("$.userId").value(CHULSOO_USER))
                .andExpect(jsonPath("$.nickname").value("철수"))
                .andExpect(jsonPath("$.message").value("2번이 마피아 같은데?"))
                .andExpect(jsonPath("$.createdAt").isString())
                .andExpect(jsonPath("$.type").value("USER"));
    }

    @Test
    void 조회는_명세_형식으로_오래된_순으로_반환한다() throws Exception {
        send(ROOM_ID, "첫 번째").andExpect(status().isCreated());
        send(ROOM_ID, "두 번째").andExpect(status().isCreated());

        mvc.perform(get("/api/v1/rooms/{roomId}/messages", ROOM_ID))
                .andExpect(status().isOk())
                .andExpect(jsonPath("$", hasSize(2)))
                .andExpect(jsonPath("$[0].userId").value(CHULSOO_USER))
                .andExpect(jsonPath("$[0].nickname").value("철수"))
                .andExpect(jsonPath("$[0].message").value("첫 번째"))
                .andExpect(jsonPath("$[1].message").value("두 번째"));
    }

    @Test
    void 로비_방_참가자가_아니면_403() throws Exception {
        loginAs(YOUNGHEE_MEMBER);
        send(ROOM_ID, "안녕")
                .andExpect(status().isForbidden())
                .andExpect(jsonPath("$.code").value("FORBIDDEN"));
    }

    @Test
    void 프로필이_없는_계정이면_400() throws Exception {
        loginAs(NO_PROFILE_MEMBER);
        send(ROOM_ID, "안녕").andExpect(status().isBadRequest());
    }

    @Test
    void 없는_방이면_404() throws Exception {
        send(999, "안녕").andExpect(status().isNotFound());
        mvc.perform(get("/api/v1/rooms/999/messages")).andExpect(status().isNotFound());
    }

    @Test
    void 빈_메시지는_400() throws Exception {
        send(ROOM_ID, "   ").andExpect(status().isBadRequest());
    }

    @Test
    void limit이_0이하면_400() throws Exception {
        mvc.perform(get("/api/v1/rooms/{roomId}/messages", ROOM_ID).param("limit", "0"))
                .andExpect(status().isBadRequest());
    }

    @Test
    void afterId_이후의_메시지만_반환한다() throws Exception {
        long first = idOf(send(ROOM_ID, "old"), "$.messageId");
        send(ROOM_ID, "new");

        mvc.perform(get("/api/v1/rooms/{roomId}/messages", ROOM_ID).param("afterId", String.valueOf(first)))
                .andExpect(jsonPath("$", hasSize(1)))
                .andExpect(jsonPath("$[0].message").value("new"));
    }

    @Test
    void 로비_입장_퇴장_이벤트는_시스템_메시지로_남는다() throws Exception {
        lobbyEvents.onJoined(new RoomPlayerJoinedEvent(ROOM_ID, YOUNGHEE_USER, "영희"));
        lobbyEvents.onLeft(new RoomPlayerLeftEvent(ROOM_ID, YOUNGHEE_USER, "영희"));

        mvc.perform(get("/api/v1/rooms/{roomId}/messages", ROOM_ID))
                .andExpect(jsonPath("$", hasSize(2)))
                .andExpect(jsonPath("$[0].type").value("SYSTEM"))
                .andExpect(jsonPath("$[0].message").value("영희님이 입장했습니다."))
                .andExpect(jsonPath("$[1].message").value("영희님이 퇴장했습니다."));
    }

    @Test
    void 공지는_시스템_메시지로_저장된다() throws Exception {
        mvc.perform(post("/api/v1/rooms/{roomId}/system-messages", ROOM_ID)
                        .contentType(MediaType.APPLICATION_JSON)
                        .content("{\"message\":\"밤이 되었습니다.\"}"))
                .andExpect(status().isCreated())
                .andExpect(jsonPath("$.type").value("SYSTEM"))
                .andExpect(jsonPath("$.message").value("밤이 되었습니다."));
    }

    @Test
    void 없는_방에는_공지할_수_없다() throws Exception {
        mvc.perform(post("/api/v1/rooms/{roomId}/system-messages", 999)
                        .contentType(MediaType.APPLICATION_JSON)
                        .content("{\"message\":\"밤이 되었습니다.\"}"))
                .andExpect(status().isNotFound());
    }
}
