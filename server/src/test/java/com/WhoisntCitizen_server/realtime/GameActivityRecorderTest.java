package com.WhoisntCitizen_server.realtime;

import com.WhoisntCitizen_server.chat.InMemoryLobbyRoomRepository;
import com.WhoisntCitizen_server.game.activity.LocalPlayerActivityTracker;
import com.WhoisntCitizen_server.lobby.domain.room.Room;
import com.WhoisntCitizen_server.lobby.domain.room.RoomPlayer;
import com.WhoisntCitizen_server.lobby.presence.RoomPresenceService;
import com.WhoisntCitizen_server.support.MutableClock;
import org.junit.jupiter.api.BeforeEach;
import org.junit.jupiter.api.Test;

import java.time.Instant;
import java.util.Map;

import static org.assertj.core.api.Assertions.assertThat;
import static org.mockito.Mockito.mock;
import static org.mockito.Mockito.verify;
import static org.mockito.Mockito.verifyNoInteractions;

class GameActivityRecorderTest {

    private static final Instant NOW = Instant.parse("2026-10-11T12:00:00Z");

    private final InMemoryLobbyRoomRepository rooms = new InMemoryLobbyRoomRepository();
    private final LocalPlayerActivityTracker tracker = new LocalPlayerActivityTracker();
    private final RoomPresenceService lobbyPresence = mock(RoomPresenceService.class);
    private final GameActivityRecorder recorder = new GameActivityRecorder(rooms, tracker, lobbyPresence, new MutableClock(NOW));
    private Room room;

    @BeforeEach
    void setUp() {
        room = new Room(3L, "방", 15L, 6);
        room.addPlayer(new RoomPlayer(15L, "철수", false));
        room.addPlayer(new RoomPlayer(16L, "영희", false));
        rooms.save(room);
    }

    @Test
    void 게임_중인_방의_참가자면_그_게임에_접속_기록을_남긴다() {
        room.startGame("g-1");

        recorder.recordAlive(3, 15);

        assertThat(tracker.lastSeen("g-1")).isEqualTo(Map.of(15L, NOW));
    }

    @Test
    void 게임_중인_방이면_대기실_기록은_남기지_않는다() {
        room.startGame("g-1");

        recorder.recordAlive(3, 15);

        verifyNoInteractions(lobbyPresence);
    }

    @Test
    void 대기_중인_방이면_게임_기록_대신_대기실_기록을_남긴다() {
        recorder.recordAlive(3, 15);

        assertThat(tracker.lastSeen("g-1")).isEmpty();
        verify(lobbyPresence).touch(3L, 15L);
    }

    @Test
    void 대기_중인_방에_없는_사람은_대기실_기록도_남기지_않는다() {
        recorder.recordAlive(3, 99);

        verifyNoInteractions(lobbyPresence);
    }

    @Test
    void 방에_없는_사람은_남기지_않는다() {
        room.startGame("g-1");

        recorder.recordAlive(3, 99);

        assertThat(tracker.lastSeen("g-1")).isEmpty();
    }

    @Test
    void 방이_없으면_남기지_않는다() {
        recorder.recordAlive(404, 15);

        assertThat(tracker.lastSeen("g-1")).isEmpty();
        verifyNoInteractions(lobbyPresence);
    }
}
