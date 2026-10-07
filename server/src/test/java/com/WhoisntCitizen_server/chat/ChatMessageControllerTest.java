package com.WhoisntCitizen_server.chat;

import com.WhoisntCitizen_server.chat.controller.ChatMessageController;
import com.WhoisntCitizen_server.chat.service.ChatLobbyEventListener;
import com.WhoisntCitizen_server.chat.service.ChatMessageService;
import com.WhoisntCitizen_server.chat.service.ChatNoticeEventListener;
import com.WhoisntCitizen_server.common.event.PirateNoticeEvent;
import com.WhoisntCitizen_server.common.exception.GlobalExceptionHandler;
import com.WhoisntCitizen_server.game.entity.Game;
import com.WhoisntCitizen_server.game.entity.GamePhase;
import com.WhoisntCitizen_server.game.entity.GamePlayer;
import com.WhoisntCitizen_server.game.repository.InMemoryGameRepository;
import com.WhoisntCitizen_server.jobs.domain.Faction;
import com.WhoisntCitizen_server.jobs.domain.RoleDefinition;
import com.WhoisntCitizen_server.lobby.domain.room.Room;
import com.WhoisntCitizen_server.lobby.domain.room.RoomPlayer;
import com.WhoisntCitizen_server.lobby.event.RoomPlayerJoinedEvent;
import com.WhoisntCitizen_server.lobby.event.RoomPlayerLeftEvent;
import com.WhoisntCitizen_server.user.entity.User;
import com.WhoisntCitizen_server.user.repository.UserRepository;
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
import java.time.Instant;
import java.util.List;
import java.util.Optional;

import static org.hamcrest.Matchers.hasItem;
import static org.hamcrest.Matchers.hasSize;
import static org.hamcrest.Matchers.not;
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
    static final long MINSU_MEMBER = 40L, MINSU_USER = 3L;       // 로비 방 참가자 (사망자 채팅 테스트에서 사망)
    static final long JISU_MEMBER = 50L, JISU_USER = 4L;         // 로비 방 참가자 (사망자 채팅 테스트에서 사망)

    static final RoleDefinition SAILOR = new RoleDefinition("CREW_SAILOR", "선원", Faction.CREW, null);
    static final RoleDefinition RAIDER = new RoleDefinition("PIRATE_RAIDER", "해적", Faction.PIRATE, null);
    static final RoleDefinition PARROT = new RoleDefinition("PIRATE_PARROT", "앵무새", Faction.PIRATE, null);

    MockMvc mvc;
    ChatLobbyEventListener lobbyEvents;
    ChatNoticeEventListener noticeEvents;
    Room room;
    InMemoryGameRepository games;

    @BeforeEach
    void setUp() {
        InMemoryLobbyRoomRepository lobbyRooms = new InMemoryLobbyRoomRepository();
        room = new Room(ROOM_ID, "마피아 1번방", CHULSOO_USER, 8);
        room.addPlayer(new RoomPlayer(CHULSOO_USER, "철수", false));
        room.addPlayer(new RoomPlayer(MINSU_USER, "민수", false));
        room.addPlayer(new RoomPlayer(JISU_USER, "지수", false));
        lobbyRooms.save(room);

        UserRepository users = mock(UserRepository.class);
        when(users.findByMemberId(CHULSOO_MEMBER))
                .thenReturn(Optional.of(User.builder().id(CHULSOO_USER).nickname("철수").build()));
        when(users.findByMemberId(YOUNGHEE_MEMBER))
                .thenReturn(Optional.of(User.builder().id(YOUNGHEE_USER).nickname("영희").build()));
        when(users.findByMemberId(NO_PROFILE_MEMBER)).thenReturn(Optional.empty());
        when(users.findByMemberId(MINSU_MEMBER))
                .thenReturn(Optional.of(User.builder().id(MINSU_USER).nickname("민수").build()));
        when(users.findByMemberId(JISU_MEMBER))
                .thenReturn(Optional.of(User.builder().id(JISU_USER).nickname("지수").build()));

        games = new InMemoryGameRepository();
        ChatMessageService messageService =
                new ChatMessageService(new InMemoryChatMessageRepository(), lobbyRooms, users, games);
        lobbyEvents = new ChatLobbyEventListener(messageService);
        noticeEvents = new ChatNoticeEventListener(messageService);

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
                .andExpect(jsonPath("$[0].userId").value(0))
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
                .andExpect(jsonPath("$.userId").value(0))
                .andExpect(jsonPath("$.message").value("밤이 되었습니다."));
    }

    @Test
    void 없는_방에는_공지할_수_없다() throws Exception {
        mvc.perform(post("/api/v1/rooms/{roomId}/system-messages", 999)
                        .contentType(MediaType.APPLICATION_JSON)
                        .content("{\"message\":\"밤이 되었습니다.\"}"))
                .andExpect(status().isNotFound());
    }

    // ---------- 게임 중 채팅: 사망자 채팅 ----------

    /** 철수(해적)·민수·지수(선원)로 게임을 시작하고, 민수와 지수를 사망시킨다. */
    private Game startGameAndKillMinsuAndJisu() {
        Game game = new Game(String.valueOf(ROOM_ID), List.of(
                new GamePlayer(CHULSOO_USER, "철수", RAIDER),
                new GamePlayer(MINSU_USER, "민수", SAILOR),
                new GamePlayer(JISU_USER, "지수", SAILOR)));
        game.changePhase(GamePhase.DAY, Instant.now().plusSeconds(60));
        games.save(game);
        room.startGame(game.getGameId());
        game.getPlayer(MINSU_USER).kill();
        game.getPlayer(JISU_USER).kill();
        return game;
    }

    private ResultActions getMessages() throws Exception {
        return mvc.perform(get("/api/v1/rooms/{roomId}/messages", ROOM_ID));
    }

    @Test
    void 사망자가_보낸_메시지는_DEAD로_저장되고_사망자에게만_보인다() throws Exception {
        startGameAndKillMinsuAndJisu();

        loginAs(MINSU_MEMBER);
        send(ROOM_ID, "억울하다...")
                .andExpect(status().isCreated())
                .andExpect(jsonPath("$.type").value("DEAD"))
                .andExpect(jsonPath("$.nickname").value("민수"));

        // 다른 사망자(직업·진영 무관)에게는 보인다
        loginAs(JISU_MEMBER);
        getMessages()
                .andExpect(jsonPath("$", hasSize(1)))
                .andExpect(jsonPath("$[0].type").value("DEAD"))
                .andExpect(jsonPath("$[0].message").value("억울하다..."));

        // 살아 있는 플레이어에게는 보이지 않는다
        loginAs(CHULSOO_MEMBER);
        getMessages().andExpect(jsonPath("$", hasSize(0)));

        // 게임 참가자가 아닌 사람(관전자 등)에게도 보이지 않는다
        loginAs(YOUNGHEE_MEMBER);
        getMessages().andExpect(jsonPath("$", hasSize(0)));
    }

    @Test
    void 살아_있는_플레이어의_메시지는_게임_중에도_모두에게_보인다() throws Exception {
        startGameAndKillMinsuAndJisu();

        send(ROOM_ID, "누가 해적이지?")
                .andExpect(status().isCreated())
                .andExpect(jsonPath("$.type").value("USER"));

        loginAs(MINSU_MEMBER);
        getMessages()
                .andExpect(jsonPath("$", hasSize(1)))
                .andExpect(jsonPath("$[0].message").value("누가 해적이지?"));
    }

    @Test
    void 숨긴_메시지가_limit보다_많아도_살아_있는_플레이어는_다음_메시지를_받는다() throws Exception {
        long first = idOf(send(ROOM_ID, "게임 전 인사"), "$.messageId");
        startGameAndKillMinsuAndJisu();

        loginAs(MINSU_MEMBER);
        for (int i = 0; i < ChatMessageService.DEFAULT_LIMIT + 10; i++) {
            send(ROOM_ID, "사망자 " + i).andExpect(status().isCreated());
        }
        loginAs(CHULSOO_MEMBER);
        send(ROOM_ID, "낮 토론 시작");

        // 폴링(afterId): 숨긴 메시지 60개를 건너뛰고 새 메시지를 받는다
        mvc.perform(get("/api/v1/rooms/{roomId}/messages", ROOM_ID).param("afterId", String.valueOf(first)))
                .andExpect(jsonPath("$", hasSize(1)))
                .andExpect(jsonPath("$[0].message").value("낮 토론 시작"));

        // 최신 목록: 숨긴 메시지 대신 그 이전 메시지로 채운다
        getMessages()
                .andExpect(jsonPath("$", hasSize(2)))
                .andExpect(jsonPath("$[0].message").value("게임 전 인사"))
                .andExpect(jsonPath("$[*].type", not(hasItem("DEAD"))));
    }

    @Test
    void 게임이_끝나면_사망자_채팅은_보이지_않고_대기실_채팅은_일반_메시지다() throws Exception {
        startGameAndKillMinsuAndJisu();
        loginAs(MINSU_MEMBER);
        send(ROOM_ID, "게임 중 사망자 채팅");

        room.finishGame(); // 대기실로 복귀

        loginAs(JISU_MEMBER);
        getMessages().andExpect(jsonPath("$", hasSize(0)));

        loginAs(MINSU_MEMBER);
        send(ROOM_ID, "다음 판 해요").andExpect(jsonPath("$.type").value("USER"));
    }

    @Test
    void 사망자는_자신이_사망하기_전에_다른_사망자들이_나눈_대화를_볼_수_없다() throws Exception {
        Game game = new Game(String.valueOf(ROOM_ID), List.of(
                new GamePlayer(CHULSOO_USER, "철수", RAIDER),
                new GamePlayer(MINSU_USER, "민수", SAILOR),
                new GamePlayer(JISU_USER, "지수", SAILOR)));
        game.changePhase(GamePhase.DAY, Instant.now().plusSeconds(60));
        games.save(game);
        room.startGame(game.getGameId());

        // 1) 민수가 먼저 사망하고 혼자 사망자 채팅
        game.getPlayer(MINSU_USER).kill();
        loginAs(MINSU_MEMBER);
        send(ROOM_ID, "지수 아직 살아 있지?").andExpect(jsonPath("$.type").value("DEAD"));
        Thread.sleep(5);

        // 2) 지수가 나중에 사망하고 사망자 채팅
        game.getPlayer(JISU_USER).kill();
        Thread.sleep(5);
        loginAs(JISU_MEMBER);
        send(ROOM_ID, "나도 죽었어").andExpect(jsonPath("$.type").value("DEAD"));

        // 지수: 자신이 죽기 전 민수의 대화는 보이지 않고, 죽은 뒤의 대화만 보인다
        getMessages()
                .andExpect(jsonPath("$", hasSize(1)))
                .andExpect(jsonPath("$[0].message").value("나도 죽었어"));

        // 민수: 자신이 죽은 뒤의 대화는 모두 보인다
        loginAs(MINSU_MEMBER);
        getMessages()
                .andExpect(jsonPath("$", hasSize(2)))
                .andExpect(jsonPath("$[0].message").value("지수 아직 살아 있지?"))
                .andExpect(jsonPath("$[1].message").value("나도 죽었어"));
    }

    // ---------- 게임 중 채팅: 밤에는 해적만 (전체 채팅에서 함께 진행) ----------

    /** 철수(해적)·민수(해적)·지수(선원) 게임을 시작하고 phase로 바꾼다. */
    private Game startPirateGame(GamePhase phase) {
        Game game = new Game(String.valueOf(ROOM_ID), List.of(
                new GamePlayer(CHULSOO_USER, "철수", RAIDER),
                new GamePlayer(MINSU_USER, "민수", RAIDER),
                new GamePlayer(JISU_USER, "지수", SAILOR)));
        game.changePhase(phase, Instant.now().plusSeconds(60));
        games.save(game);
        room.startGame(game.getGameId());
        return game;
    }

    @Test
    void 밤에_해적이_입력한_채팅은_전체_채팅으로_오가지만_해적에게만_보인다() throws Exception {
        startPirateGame(GamePhase.NIGHT);

        send(ROOM_ID, "오늘 밤은 지수를 노리자")
                .andExpect(status().isCreated())
                .andExpect(jsonPath("$.type").value("USER"))
                .andExpect(jsonPath("$.nightChat").value(true));

        loginAs(MINSU_MEMBER); // 다른 해적
        getMessages()
                .andExpect(jsonPath("$", hasSize(1)))
                .andExpect(jsonPath("$[0].type").value("USER"))
                .andExpect(jsonPath("$[0].nightChat").value(true))
                .andExpect(jsonPath("$[0].message").value("오늘 밤은 지수를 노리자"));

        loginAs(JISU_MEMBER); // 선원
        getMessages().andExpect(jsonPath("$", hasSize(0)));

        loginAs(YOUNGHEE_MEMBER); // 게임 참가자 아님
        getMessages().andExpect(jsonPath("$", hasSize(0)));
    }

    @Test
    void 밤에는_해적이_아닌_생존자는_채팅을_입력할_수_없다() throws Exception {
        startPirateGame(GamePhase.NIGHT);

        loginAs(JISU_MEMBER);
        send(ROOM_ID, "누구 있어요?")
                .andExpect(status().isForbidden())
                .andExpect(jsonPath("$.message").value(ChatMessageService.NIGHT_BLOCKED_MESSAGE));

        loginAs(CHULSOO_MEMBER);
        getMessages().andExpect(jsonPath("$", hasSize(0))); // 저장되지 않음
    }

    @Test
    void 낮에는_누구나_입력하고_모두에게_보인다() throws Exception {
        startPirateGame(GamePhase.DAY);

        send(ROOM_ID, "저는 선원입니다")
                .andExpect(jsonPath("$.type").value("USER"))
                .andExpect(jsonPath("$.nightChat").value(false));
        loginAs(JISU_MEMBER);
        send(ROOM_ID, "저도요").andExpect(status().isCreated());

        getMessages().andExpect(jsonPath("$", hasSize(2)));
    }

    @Test
    void 해적은_밤에_오간_채팅을_낮에도_사망한_뒤에도_읽을_수_있고_선원은_낮에도_읽을_수_없다() throws Exception {
        Game game = startPirateGame(GamePhase.NIGHT);
        send(ROOM_ID, "밤의 작전");

        game.changePhase(GamePhase.DAY, Instant.now().plusSeconds(60));
        game.getPlayer(MINSU_USER).kill();

        loginAs(MINSU_MEMBER); // 낮 + 사망한 해적
        getMessages()
                .andExpect(jsonPath("$", hasSize(1)))
                .andExpect(jsonPath("$[0].message").value("밤의 작전"));

        loginAs(JISU_MEMBER); // 낮이 되어도 선원에게는 보이지 않는다
        getMessages().andExpect(jsonPath("$", hasSize(0)));
    }

    @Test
    void 사망자는_밤에도_사망자_채팅을_할_수_있다() throws Exception {
        Game game = startPirateGame(GamePhase.NIGHT);
        game.getPlayer(MINSU_USER).kill(); // 사망한 해적
        game.getPlayer(JISU_USER).kill();  // 사망한 선원

        loginAs(JISU_MEMBER);
        send(ROOM_ID, "밤에도 말할 수 있네").andExpect(jsonPath("$.type").value("DEAD"));
        loginAs(MINSU_MEMBER);
        send(ROOM_ID, "나 죽었어").andExpect(jsonPath("$.type").value("DEAD"));

        loginAs(CHULSOO_MEMBER); // 살아 있는 해적에게는 사망자 채팅이 보이지 않는다
        getMessages().andExpect(jsonPath("$", hasSize(0)));
    }

    @Test
    void 앵무새는_접선한_뒤부터_밤에_입력할_수_있고_접선_뒤의_밤_채팅만_읽는다() throws Exception {
        Game game = new Game(String.valueOf(ROOM_ID), List.of(
                new GamePlayer(CHULSOO_USER, "철수", RAIDER),
                new GamePlayer(MINSU_USER, "민수", PARROT),
                new GamePlayer(JISU_USER, "지수", SAILOR)));
        game.changePhase(GamePhase.NIGHT, Instant.now().plusSeconds(60));
        games.save(game);
        room.startGame(game.getGameId());

        send(ROOM_ID, "앵무새는 누구지?").andExpect(status().isCreated());

        // 접선 전: 밤 채팅이 보이지 않고 입력도 할 수 없다
        loginAs(MINSU_MEMBER);
        getMessages().andExpect(jsonPath("$", hasSize(0)));
        send(ROOM_ID, "조용한 밤이네요").andExpect(status().isForbidden());

        Thread.sleep(5);
        game.getPlayer(MINSU_USER).markContacted(Instant.now());
        Thread.sleep(5);

        // 접선 후: 입력할 수 있고, 접선 뒤의 밤 채팅만 읽는다
        send(ROOM_ID, "접선 완료").andExpect(status().isCreated());
        getMessages()
                .andExpect(jsonPath("$", hasSize(1)))
                .andExpect(jsonPath("$[0].message").value("접선 완료"));

        loginAs(CHULSOO_MEMBER); // 해적은 처음부터 모든 밤 채팅을 읽는다
        getMessages().andExpect(jsonPath("$", hasSize(2)));
    }

    @Test
    void 게임이_끝나면_밤에_오간_채팅은_보이지_않는다() throws Exception {
        startPirateGame(GamePhase.NIGHT);
        send(ROOM_ID, "밤의 작전");

        room.finishGame();

        getMessages().andExpect(jsonPath("$", hasSize(0)));
    }

    @Test
    void 해적_전용_시스템_메시지는_해적에게만_보인다() throws Exception {
        Game game = startPirateGame(GamePhase.NIGHT);
        noticeEvents.onPirateNotice(new PirateNoticeEvent(String.valueOf(ROOM_ID), game.getGameId(),
                "철수님이 지수님을 공격 대상으로 골랐습니다."));

        loginAs(MINSU_MEMBER); // 해적
        getMessages()
                .andExpect(jsonPath("$", hasSize(1)))
                .andExpect(jsonPath("$[0].type").value("SYSTEM"))
                .andExpect(jsonPath("$[0].message").value("철수님이 지수님을 공격 대상으로 골랐습니다."));

        loginAs(JISU_MEMBER); // 선원
        getMessages().andExpect(jsonPath("$", hasSize(0)));
    }

    // ---------- 게임 중 채팅: 세이렌 팀 (세이렌만 말하고 팀원은 읽기만) ----------

    static final RoleDefinition SIREN = new RoleDefinition("NEUTRAL_SIREN", "세이렌", Faction.NEUTRAL, null);

    @Test
    void 밤에_세이렌이_입력한_채팅은_유혹당한_뒤의_팀원에게만_보이고_팀원은_밤에_입력할_수_없다() throws Exception {
        Game game = new Game(String.valueOf(ROOM_ID), List.of(
                new GamePlayer(CHULSOO_USER, "철수", SIREN),
                new GamePlayer(MINSU_USER, "민수", SAILOR),
                new GamePlayer(JISU_USER, "지수", RAIDER)));
        game.changePhase(GamePhase.NIGHT, Instant.now().plusSeconds(60));
        games.save(game);
        room.startGame(game.getGameId());

        send(ROOM_ID, "유혹 전의 노래")
                .andExpect(status().isCreated())
                .andExpect(jsonPath("$.sirenChat").value(true))
                .andExpect(jsonPath("$.nightChat").value(false));

        Thread.sleep(5);
        game.getPlayer(MINSU_USER).joinSirenTeam(Instant.now());
        Thread.sleep(5);

        send(ROOM_ID, "이제 우리 팀이야").andExpect(status().isCreated());

        loginAs(MINSU_MEMBER); // 유혹당한 팀원: 유혹 뒤의 메시지만 읽고, 밤에 입력할 수 없다
        getMessages()
                .andExpect(jsonPath("$", hasSize(1)))
                .andExpect(jsonPath("$[0].message").value("이제 우리 팀이야"))
                .andExpect(jsonPath("$[0].sirenChat").value(true));
        send(ROOM_ID, "알겠어").andExpect(status().isForbidden());

        loginAs(JISU_MEMBER); // 해적: 세이렌 채팅은 보이지 않는다
        getMessages().andExpect(jsonPath("$", hasSize(0)));

        game.getPlayer(MINSU_USER).kill(); // 사망한 팀원도 계속 읽는다
        loginAs(MINSU_MEMBER);
        getMessages().andExpect(jsonPath("$", hasSize(1)));
    }
}
