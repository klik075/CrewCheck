package com.WhoisntCitizen_server.lobby.service;

import com.WhoisntCitizen_server.chat.InMemoryLobbyRoomRepository;
import com.WhoisntCitizen_server.game.activity.LocalPlayerActivityTracker;
import com.WhoisntCitizen_server.game.dto.GameStateResponse;
import com.WhoisntCitizen_server.game.entity.Game;
import com.WhoisntCitizen_server.game.lock.LocalGameLock;
import com.WhoisntCitizen_server.game.repository.InMemoryGameRepository;
import com.WhoisntCitizen_server.game.repository.snapshot.GameSnapshot;
import com.WhoisntCitizen_server.game.service.GameFlowService;
import com.WhoisntCitizen_server.game.service.GameService;
import com.WhoisntCitizen_server.lobby.domain.room.Room;
import com.WhoisntCitizen_server.lobby.domain.room.RoomPlayer;
import com.WhoisntCitizen_server.lobby.dto.RoomPlayerResponseDto;
import com.WhoisntCitizen_server.lobby.lock.LocalRoomLock;
import com.WhoisntCitizen_server.support.TestRoles;
import com.WhoisntCitizen_server.user.dto.UserDto;
import com.WhoisntCitizen_server.user.entity.User;
import com.WhoisntCitizen_server.user.repository.UserRepository;
import jakarta.validation.Validation;
import org.junit.jupiter.api.Test;
import org.springframework.data.redis.serializer.JacksonJsonRedisSerializer;
import tools.jackson.databind.json.JsonMapper;

import java.nio.charset.StandardCharsets;
import java.time.Clock;
import java.util.List;
import java.util.Optional;

import static org.assertj.core.api.Assertions.assertThat;
import static org.mockito.Mockito.*;

class RoomProfileSkinTest {
    @Test
    void 로비_프로필이_게임_응답과_저장_복원까지_유지된다() {
        var rooms = new InMemoryLobbyRoomRepository();
        var users = mock(UserRepository.class);
        var games = new InMemoryGameRepository();
        var gameService = new GameService(games, TestRoles.assigner(7), mock(GameFlowService.class),
                new LocalGameLock(), new LocalPlayerActivityTracker(), Clock.systemUTC());
        var roomService = new RoomService(rooms, users, new LocalRoomLock(), gameService, event -> {});
        var room = new Room(1L, "스킨 테스트", 1L, 4);
        for (long id = 1; id <= 4; id++) room.addPlayer(new RoomPlayer(id, "p" + id, true));
        rooms.save(room);
        // DB 반환 순서는 좌석 순서와 다를 수 있다. 같은 스킨을 고르는 것도 허용한다.
        when(users.findAllById(List.of(1L, 2L, 3L, 4L))).thenReturn(List.of(
                User.builder().id(3L).profileImageId(19).build(),
                User.builder().id(1L).profileImageId(13).build(),
                User.builder().id(4L).profileImageId(0).build(),
                User.builder().id(2L).profileImageId(13).build()));

        String gameId = roomService.startGame(1L, 1L).gameId();
        var state = gameService.getState(gameId);
        assertThat(state.players()).extracting(GameStateResponse.PlayerView::profileImageId)
                .containsExactly(13, 13, 19, 0);
        assertThat(state.players()).extracting(GameStateResponse.PlayerView::playerId)
                .containsExactly(1L, 2L, 3L, 4L);
        gameService.getState(gameId);
        verify(users, times(1)).findAllById(List.of(1L, 2L, 3L, 4L));

        var mapper = JsonMapper.builder().build();
        Game original = games.findById(gameId).orElseThrow();
        Game restored = mapper.readValue(mapper.writeValueAsString(GameSnapshot.from(original)), GameSnapshot.class).toGame();
        assertThat(GameStateResponse.from(restored, Clock.systemUTC().instant()).players()).isEqualTo(state.players());
    }

    @Test
    void 대기실_참가자_응답에_입장할_때의_프로필_이미지가_들어간다() {
        var rooms = new InMemoryLobbyRoomRepository();
        var users = mock(UserRepository.class);
        var gameService = new GameService(new InMemoryGameRepository(), TestRoles.assigner(7), mock(GameFlowService.class),
                new LocalGameLock(), new LocalPlayerActivityTracker(), Clock.systemUTC());
        var roomService = new RoomService(rooms, users, new LocalRoomLock(), gameService, event -> {});
        var room = new Room(1L, "대기실", 1L, 4);
        room.addPlayer(new RoomPlayer(1L, "방장", false, 13));
        rooms.save(room);
        when(users.findById(2L)).thenReturn(Optional.of(User.builder().id(2L).nickname("철수").profileImageId(19).build()));

        roomService.joinRoom(1L, 2L, null);

        assertThat(roomService.getRoom(1L).getPlayers()).extracting(RoomPlayerResponseDto::getProfileImageId)
                .containsExactly(13, 19);
        assertThat(roomService.getPlayers(1L)).extracting(RoomPlayerResponseDto::getProfileImageId)
                .containsExactly(13, 19);
    }

    @Test
    void 프로필_이미지가_없던_방_데이터는_기본_아바타로_읽는다() {
        var serializer = new JacksonJsonRedisSerializer<>(Room.class);
        var room = new Room(1L, "저장", 1L, 4);
        room.addPlayer(new RoomPlayer(1L, "방장", false, 13));
        assertThat(serializer.deserialize(serializer.serialize(room)).getPlayers())
                .extracting(RoomPlayer::getProfileImageId).containsExactly(13);

        String saved = new String(serializer.serialize(room), StandardCharsets.UTF_8);
        String legacy = saved.replace(",\"profileImageId\":13", "");
        assertThat(legacy).doesNotContain("profileImageId");
        assertThat(serializer.deserialize(legacy.getBytes(StandardCharsets.UTF_8)).getPlayers())
                .extracting(RoomPlayer::getProfileImageId).containsExactly(0);
    }

    @Test
    void 삭제한_20번은_선택할_수_없고_19번까지_허용한다() {
        try (var factory = Validation.buildDefaultValidatorFactory()) {
            var validator = factory.getValidator();
            assertThat(validator.validate(new UserDto.UpdateProfileImageRequest(19))).isEmpty();
            assertThat(validator.validate(new UserDto.UpdateProfileImageRequest(20))).isNotEmpty();
            assertThat(validator.validate(new UserDto.UpdateProfileImageRequest(-1))).isNotEmpty();
        }
    }
}
