package com.WhoisntCitizen_server.lobby.service;

import com.WhoisntCitizen_server.chat.InMemoryLobbyRoomRepository;
import com.WhoisntCitizen_server.game.service.GameService;
import com.WhoisntCitizen_server.lobby.domain.room.Room;
import com.WhoisntCitizen_server.lobby.dto.RoomResponseDto;
import com.WhoisntCitizen_server.lobby.lock.LocalRoomLock;
import com.WhoisntCitizen_server.user.repository.UserRepository;
import org.junit.jupiter.api.BeforeEach;
import org.junit.jupiter.api.Test;

import java.util.Collection;
import java.util.List;
import java.util.Set;

import static org.assertj.core.api.Assertions.assertThat;
import static org.mockito.Mockito.mock;

/** 방 목록·검색이 findAllByIds(MGET)로 바뀐 뒤에도 같은 결과를 내는지 */
class RoomServiceRoomListTest {

    private InMemoryLobbyRoomRepository rooms;
    private RoomService roomService;

    @BeforeEach
    void setUp() {
        rooms = new InMemoryLobbyRoomRepository();
        roomService = new RoomService(rooms, mock(UserRepository.class), new LocalRoomLock(),
                mock(GameService.class), event -> { });
        rooms.save(new Room(1L, "초보만 오세요", 10L, 8));
        rooms.save(new Room(2L, "고수 전용", 20L, 8));
        rooms.save(new Room(3L, "초보 환영", 30L, 8));
    }

    @Test
    void 전체_목록은_모든_방을_돌려준다() {
        assertThat(roomService.getRooms()).extracting(RoomResponseDto::getId)
                .containsExactlyInAnyOrder(1L, 2L, 3L);
    }

    @Test
    void 제목_검색은_일치하는_방만_돌려준다() {
        assertThat(roomService.getRooms("초보")).extracting(RoomResponseDto::getId)
                .containsExactlyInAnyOrder(1L, 3L);
    }

    @Test
    void 방_목록을_한_번에_읽는다() {
        CountingRepository counting = new CountingRepository();
        counting.save(new Room(1L, "가", 10L, 8));
        counting.save(new Room(2L, "나", 20L, 8));
        RoomService service = new RoomService(counting, mock(UserRepository.class), new LocalRoomLock(),
                mock(GameService.class), event -> { });

        service.getRooms();

        assertThat(counting.findAllByIdsCalls).isEqualTo(1);
        assertThat(counting.findByIdCalls).isZero(); // 방마다 따로 읽지 않는다 (N+1 방지)
    }

    @Test
    void ID_목록에는_있지만_지워진_방은_빠진다() {
        InMemoryLobbyRoomRepository stale = new InMemoryLobbyRoomRepository() {
            @Override
            public Set<String> findAllRoomIds() {
                return Set.of("1", "99"); // 99번은 목록에만 남고 방 본문은 지워진 상태
            }
        };
        stale.save(new Room(1L, "가", 10L, 8));
        RoomService service = new RoomService(stale, mock(UserRepository.class), new LocalRoomLock(),
                mock(GameService.class), event -> { });

        assertThat(service.getRooms()).extracting(RoomResponseDto::getId).containsExactly(1L);
    }

    /** findById / findAllByIds 호출 횟수를 센다 */
    private static class CountingRepository extends InMemoryLobbyRoomRepository {
        int findByIdCalls;
        int findAllByIdsCalls;

        @Override
        public Room findById(Long roomId) {
            findByIdCalls++;
            return super.findById(roomId);
        }

        @Override
        public List<Room> findAllByIds(Collection<Long> roomIds) {
            findAllByIdsCalls++;
            return super.findAllByIds(roomIds);
        }
    }
}
