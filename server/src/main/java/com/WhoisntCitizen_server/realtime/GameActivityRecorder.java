package com.WhoisntCitizen_server.realtime;

import com.WhoisntCitizen_server.game.activity.PlayerActivityTracker;
import com.WhoisntCitizen_server.lobby.domain.room.Room;
import com.WhoisntCitizen_server.lobby.repository.LobbyRoomRepository;

import java.time.Clock;
import java.util.Objects;

/**
 * 방 → 진행 중인 게임 id를 찾아 PlayerActivityTracker.touch를 부른다. (상태 조회 GET이 하던 touch와 같은 기록)
 *
 * 참가자 확인: 방에 들어 있는 사람만 기록한다. 게임 중 연결이 끊겨 내보내진 사람은 방에서 빠지므로 기록되지 않는다.
 * (PlayerActivityTracker 계약: 참가자인지는 호출하는 쪽에서 확인)
 * 방은 잠금 없이 읽는다. 기록은 "더 늦은 시각만 반영"이라 순서가 바뀌어도 안전하다.
 */
public class GameActivityRecorder implements RealtimeActivityRecorder {

    private final LobbyRoomRepository roomRepository;
    private final PlayerActivityTracker activityTracker;
    private final Clock clock;

    public GameActivityRecorder(LobbyRoomRepository roomRepository, PlayerActivityTracker activityTracker, Clock clock) {
        this.roomRepository = Objects.requireNonNull(roomRepository, "roomRepository");
        this.activityTracker = Objects.requireNonNull(activityTracker, "activityTracker");
        this.clock = Objects.requireNonNull(clock, "clock");
    }

    @Override
    public void recordAlive(long roomId, long userId) {
        Room room = roomRepository.findById(roomId);
        if (room == null || !room.isInGame() || room.getGameId() == null || !room.containsPlayer(userId)) {
            return;
        }
        activityTracker.touch(room.getGameId(), userId, clock.instant());
    }
}
