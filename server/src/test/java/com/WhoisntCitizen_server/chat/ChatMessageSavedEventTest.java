package com.WhoisntCitizen_server.chat;

import com.WhoisntCitizen_server.chat.event.ChatMessageSavedEvent;
import com.WhoisntCitizen_server.chat.service.ChatLobbyEventListener;
import com.WhoisntCitizen_server.chat.service.ChatMessageService;
import com.WhoisntCitizen_server.chat.service.ChatNoticeEventListener;
import com.WhoisntCitizen_server.common.event.PirateNoticeEvent;
import com.WhoisntCitizen_server.game.entity.Game;
import com.WhoisntCitizen_server.game.entity.GamePhase;
import com.WhoisntCitizen_server.game.entity.GamePlayer;
import com.WhoisntCitizen_server.game.lock.LocalGameLock;
import com.WhoisntCitizen_server.game.repository.InMemoryGameRepository;
import com.WhoisntCitizen_server.jobs.domain.Faction;
import com.WhoisntCitizen_server.jobs.domain.RoleDefinition;
import com.WhoisntCitizen_server.lobby.domain.room.Room;
import com.WhoisntCitizen_server.lobby.domain.room.RoomPlayer;
import com.WhoisntCitizen_server.lobby.event.RoomPlayerJoinedEvent;
import com.WhoisntCitizen_server.user.entity.User;
import com.WhoisntCitizen_server.user.repository.UserRepository;
import org.junit.jupiter.api.BeforeEach;
import org.junit.jupiter.api.Test;

import java.time.Instant;
import java.util.ArrayList;
import java.util.List;
import java.util.Optional;
import java.util.Set;

import static org.assertj.core.api.Assertions.assertThat;
import static org.mockito.Mockito.mock;
import static org.mockito.Mockito.when;

/**
 * 4-6: 채팅 메시지를 저장할 때 ChatMessageSavedEvent가 "볼 수 있는 사람"을 담아 발행되는지 확인한다.
 * 볼 수 있는 사람은 채팅 조회(GET)의 규칙과 같아야 한다. (ChatMessageControllerTest의 보이는 규칙 테스트와 짝)
 *  - 철수(1), 민수(3), 지수(4)가 방 1에 있다
 */
class ChatMessageSavedEventTest {

    static final long ROOM_ID = 1L;
    static final long CHULSOO_MEMBER = 10L, CHULSOO_USER = 1L;
    static final long MINSU_MEMBER = 40L, MINSU_USER = 3L;
    static final long JISU_MEMBER = 50L, JISU_USER = 4L;

    static final RoleDefinition SAILOR = new RoleDefinition("CREW_SAILOR", "선원", Faction.CREW, null);
    static final RoleDefinition RAIDER = new RoleDefinition("PIRATE_RAIDER", "해적", Faction.PIRATE, null);
    static final RoleDefinition SIREN = new RoleDefinition("NEUTRAL_SIREN", "세이렌", Faction.NEUTRAL, null);

    private final List<ChatMessageSavedEvent> events = new ArrayList<>();
    private ChatMessageService service;
    private ChatLobbyEventListener lobbyEvents;
    private ChatNoticeEventListener noticeEvents;
    private Room room;
    private InMemoryGameRepository games;

    @BeforeEach
    void setUp() {
        InMemoryLobbyRoomRepository lobbyRooms = new InMemoryLobbyRoomRepository();
        room = new Room(ROOM_ID, "마피아 1번방", CHULSOO_USER, 8);
        room.addPlayer(new RoomPlayer(CHULSOO_USER, "철수", false));
        room.addPlayer(new RoomPlayer(MINSU_USER, "민수", false));
        room.addPlayer(new RoomPlayer(JISU_USER, "지수", false));
        lobbyRooms.save(room);

        UserRepository users = mock(UserRepository.class);
        when(users.findByMemberId(CHULSOO_MEMBER)).thenReturn(Optional.of(User.builder().id(CHULSOO_USER).nickname("철수").build()));
        when(users.findByMemberId(MINSU_MEMBER)).thenReturn(Optional.of(User.builder().id(MINSU_USER).nickname("민수").build()));
        when(users.findByMemberId(JISU_MEMBER)).thenReturn(Optional.of(User.builder().id(JISU_USER).nickname("지수").build()));

        games = new InMemoryGameRepository();
        service = new ChatMessageService(new InMemoryChatMessageRepository(), lobbyRooms, users, games,
                new LocalGameLock(), event -> {
                    if (event instanceof ChatMessageSavedEvent saved) {
                        events.add(saved);
                    }
                });
        lobbyEvents = new ChatLobbyEventListener(service);
        noticeEvents = new ChatNoticeEventListener(service);
    }

    private Game startGame(GamePhase phase, RoleDefinition chulsoo, RoleDefinition minsu, RoleDefinition jisu) {
        Game game = new Game(String.valueOf(ROOM_ID), List.of(
                new GamePlayer(CHULSOO_USER, "철수", chulsoo),
                new GamePlayer(MINSU_USER, "민수", minsu),
                new GamePlayer(JISU_USER, "지수", jisu)));
        game.changePhase(phase, Instant.now().plusSeconds(60));
        games.save(game);
        room.startGame(game.getGameId());
        return game;
    }

    private ChatMessageSavedEvent last() {
        assertThat(events).isNotEmpty();
        return events.get(events.size() - 1);
    }

    @Test
    void 일반_채팅은_모두에게_보이는_메시지로_발행한다() {
        long messageId = service.send(ROOM_ID, CHULSOO_MEMBER, "안녕").messageId();

        assertThat(last()).isEqualTo(new ChatMessageSavedEvent(ROOM_ID, messageId, null));
        assertThat(last().visibleToEveryone()).isTrue();
    }

    @Test
    void 시스템_안내도_모두에게_보이는_메시지로_발행한다() {
        lobbyEvents.onJoined(new RoomPlayerJoinedEvent(ROOM_ID, MINSU_USER, "민수"));

        assertThat(last().visibleToEveryone()).isTrue();
    }

    @Test
    void 게임_중_낮의_생존자_채팅은_모두에게_보인다() {
        startGame(GamePhase.DAY, RAIDER, SAILOR, SAILOR);

        service.send(ROOM_ID, JISU_MEMBER, "누가 해적이지?");

        assertThat(last().visibleToEveryone()).isTrue();
    }

    @Test
    void 사망자_채팅은_그_게임의_사망자에게만() {
        Game game = startGame(GamePhase.DAY, RAIDER, SAILOR, SAILOR);
        game.getPlayer(MINSU_USER).kill();
        game.getPlayer(JISU_USER).kill();

        service.send(ROOM_ID, MINSU_MEMBER, "억울하다...");

        assertThat(last().audience()).containsExactlyInAnyOrder(MINSU_USER, JISU_USER);
    }

    @Test
    void 밤의_해적_채팅은_해적에게만() {
        startGame(GamePhase.NIGHT, RAIDER, RAIDER, SAILOR);

        service.send(ROOM_ID, CHULSOO_MEMBER, "오늘 밤은 지수를 노리자");

        assertThat(last().audience()).containsExactlyInAnyOrder(CHULSOO_USER, MINSU_USER);
    }

    @Test
    void 해적_안내는_해적에게만() {
        Game game = startGame(GamePhase.NIGHT, RAIDER, RAIDER, SAILOR);

        noticeEvents.onPirateNotice(new PirateNoticeEvent(String.valueOf(ROOM_ID), game.getGameId(),
                "철수님이 지수님을 공격 대상으로 골랐습니다."));

        assertThat(last().audience()).containsExactlyInAnyOrder(CHULSOO_USER, MINSU_USER);
    }

    @Test
    void 끝난_게임의_해적_안내는_아무에게도_보내지_않는다() {
        Game game = startGame(GamePhase.NIGHT, RAIDER, RAIDER, SAILOR);
        games.delete(game.getGameId());

        noticeEvents.onPirateNotice(new PirateNoticeEvent(String.valueOf(ROOM_ID), game.getGameId(), "늦은 안내"));

        assertThat(last().audience()).isEqualTo(Set.of());
    }

    @Test
    void 세이렌_채팅은_지금_세이렌_팀인_사람에게만() {
        Game game = startGame(GamePhase.NIGHT, SIREN, SAILOR, RAIDER);

        service.send(ROOM_ID, CHULSOO_MEMBER, "유혹 전의 노래");
        assertThat(last().audience()).containsExactly(CHULSOO_USER);

        game.getPlayer(MINSU_USER).joinSirenTeam(Instant.now());
        service.send(ROOM_ID, CHULSOO_MEMBER, "이제 우리 팀이야");
        assertThat(last().audience()).containsExactlyInAnyOrder(CHULSOO_USER, MINSU_USER);
    }
}
