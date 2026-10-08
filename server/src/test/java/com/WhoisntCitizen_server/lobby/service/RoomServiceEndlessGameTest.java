package com.WhoisntCitizen_server.lobby.service;

import com.WhoisntCitizen_server.chat.InMemoryLobbyRoomRepository;
import com.WhoisntCitizen_server.game.service.GameService;
import com.WhoisntCitizen_server.lobby.domain.room.Room;
import com.WhoisntCitizen_server.lobby.domain.room.RoomPlayer;
import com.WhoisntCitizen_server.lobby.event.RoomDeletedEvent;
import com.WhoisntCitizen_server.lobby.lock.LocalRoomLock;
import com.WhoisntCitizen_server.user.entity.User;
import com.WhoisntCitizen_server.user.repository.UserRepository;
import org.junit.jupiter.api.BeforeEach;
import org.junit.jupiter.api.Test;

import java.util.ArrayList;
import java.util.List;
import java.util.Optional;

import static org.assertj.core.api.Assertions.assertThat;
import static org.mockito.Mockito.mock;
import static org.mockito.Mockito.when;

/** 끝나지 않는 게임 방지의 로비 쪽: 연결이 끊긴 플레이어 제외, 취소된 게임의 방 삭제, 방 삭제 알림 */
class RoomServiceEndlessGameTest {

    private static final long ROOM_ID = 1L;
    private static final String GAME_ID = "game-1";

    private InMemoryLobbyRoomRepository rooms;
    private UserRepository users;
    private GameService gameService;
    private final List<Object> events = new ArrayList<>();
    private RoomService roomService;

    @BeforeEach
    void setUp() {
        rooms = new InMemoryLobbyRoomRepository();
        users = mock(UserRepository.class);
        gameService = mock(GameService.class);
        roomService = new RoomService(rooms, users, new LocalRoomLock(), gameService, events::add);
    }

    /** 방장 10, 참가자 20·30. gameId가 있으면 그 게임이 진행 중인 방 */
    private Room saveRoom(String gameId) {
        Room room = new Room(ROOM_ID, "방", 10L, 8);
        room.addPlayer(new RoomPlayer(10L, "방장", false));
        room.addPlayer(new RoomPlayer(20L, "철수", false));
        room.addPlayer(new RoomPlayer(30L, "영희", false));
        if (gameId != null) {
            room.startGame(gameId);
        }
        rooms.save(room);
        return room;
    }

    @Test
    void 연결이_끊긴_플레이어를_방에서_빼고_방장이면_위임한다() {
        saveRoom(GAME_ID);

        roomService.removeDepartedPlayers(ROOM_ID, GAME_ID, List.of(10L, 30L));

        Room room = rooms.findById(ROOM_ID);
        assertThat(room.getPlayers()).extracting(RoomPlayer::getUserId).containsExactly(20L);
        assertThat(room.getHostUserId()).isEqualTo(20L);
        assertThat(room.isInGame()).isTrue();
        assertThat(events).isEmpty();
    }

    @Test
    void 게임이_끝나_대기_상태로_돌아간_뒤에_와도_뺀다() {
        Room room = saveRoom(GAME_ID);
        room.finishGame();
        rooms.save(room);

        roomService.removeDepartedPlayers(ROOM_ID, GAME_ID, List.of(30L));

        assertThat(rooms.findById(ROOM_ID).containsPlayer(30L)).isFalse();
    }

    @Test
    void 같은_방에서_다른_게임이_시작됐으면_빼지_않는다() {
        saveRoom("game-2");

        roomService.removeDepartedPlayers(ROOM_ID, GAME_ID, List.of(30L));

        assertThat(rooms.findById(ROOM_ID).containsPlayer(30L)).isTrue();
    }

    @Test
    void 모두_빠져_방이_비면_방을_지우고_알린다() {
        saveRoom(GAME_ID);

        roomService.removeDepartedPlayers(ROOM_ID, GAME_ID, List.of(10L, 20L, 30L));

        assertThat(rooms.findById(ROOM_ID)).isNull();
        assertThat(events).containsExactly(new RoomDeletedEvent(ROOM_ID));
    }

    @Test
    void 취소된_게임의_방을_지우고_알린다() {
        saveRoom(GAME_ID);

        boolean deleted = roomService.deleteRoomOfCancelledGame(ROOM_ID, GAME_ID);

        assertThat(deleted).isTrue();
        assertThat(rooms.findById(ROOM_ID)).isNull();
        assertThat(events).containsExactly(new RoomDeletedEvent(ROOM_ID));
    }

    @Test
    void 방이_이미_다른_게임으로_넘어갔으면_지우지_않는다() {
        saveRoom("game-2");

        boolean deleted = roomService.deleteRoomOfCancelledGame(ROOM_ID, GAME_ID);

        assertThat(deleted).isFalse();
        assertThat(rooms.findById(ROOM_ID)).isNotNull();
        assertThat(events).isEmpty();
    }

    @Test
    void 취소된_게임이_메모리에_남아_있는_동안은_방을_대기_상태로_되돌리지_않는다() {
        saveRoom(GAME_ID);
        when(gameService.keepsRoomInGame(GAME_ID)).thenReturn(true);

        roomService.getRoom(ROOM_ID);

        assertThat(rooms.findById(ROOM_ID).isInGame()).isTrue();
    }

    @Test
    void 마지막_사람이_나가_방이_지워지면_알린다() {
        Room room = new Room(ROOM_ID, "방", 10L, 8);
        room.addPlayer(new RoomPlayer(10L, "방장", false));
        rooms.save(room);
        when(users.findByMemberId(100L)).thenReturn(Optional.of(User.builder().id(10L).nickname("방장").build()));

        roomService.leaveRoom(ROOM_ID, 100L);

        assertThat(rooms.findById(ROOM_ID)).isNull();
        assertThat(events).containsExactly(new RoomDeletedEvent(ROOM_ID));
    }
}
