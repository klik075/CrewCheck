package com.WhoisntCitizen_server.realtime;

import com.WhoisntCitizen_server.game.activity.PlayerActivityTracker;
import com.WhoisntCitizen_server.lobby.domain.room.Room;
import com.WhoisntCitizen_server.lobby.presence.RoomPresenceService;
import com.WhoisntCitizen_server.lobby.repository.LobbyRoomRepository;

import java.time.Clock;
import java.util.Objects;

/**
 * WebSocket이 살아 있다는 것(ping, 방 구독)을 접속 기록에 남긴다. 방 상태에 따라 기록하는 곳이 다르다.
 *  - 게임 중인 방: 게임 접속 기록 PlayerActivityTracker.touch (InactivePlayerMonitor가 읽는다. 상태 조회 GET이 하던 touch와 같은 기록)
 *  - 대기 중인 방: 대기실 접속 기록 RoomPresenceService.touch (대기실 연결 끊김 검사가 읽는다. 방 조회 GET이 하던 touch와 같은 기록)
 *
 * 대기실 기록은 멀티서버 3단계(재연결 정리)에서 더했다. 그 전에는 대기실이 방 조회 GET(15초마다)에만 의존해서
 * 조회가 몇 번 늦으면 멀쩡한 사람이 빠질 수 있었다. 이제 ping(10초마다)도 대기실 기록에 남는다.
 *
 * 참가자 확인: 방에 들어 있는 사람만 기록한다. 게임 중 연결이 끊겨 내보내진 사람은 방에서 빠지므로 기록되지 않는다.
 * (PlayerActivityTracker 계약: 참가자인지는 호출하는 쪽에서 확인)
 * 방은 잠금 없이 읽는다. 두 기록 모두 "더 늦은 시각만 반영"하거나 같은 시각을 덮어쓸 뿐이라 순서가 바뀌어도 안전하다.
 * 기록이 어느 서버에서 일어나도 Redis(서버 여러 대일 때)에 남으므로 모든 서버가 같은 기록을 본다.
 */
public class GameActivityRecorder implements RealtimeActivityRecorder {

    private final LobbyRoomRepository roomRepository;
    private final PlayerActivityTracker activityTracker;
    private final RoomPresenceService lobbyPresence;
    private final Clock clock;

    public GameActivityRecorder(LobbyRoomRepository roomRepository, PlayerActivityTracker activityTracker,
                                RoomPresenceService lobbyPresence, Clock clock) {
        this.roomRepository = Objects.requireNonNull(roomRepository, "roomRepository");
        this.activityTracker = Objects.requireNonNull(activityTracker, "activityTracker");
        this.lobbyPresence = Objects.requireNonNull(lobbyPresence, "lobbyPresence");
        this.clock = Objects.requireNonNull(clock, "clock");
    }

    @Override
    public void recordAlive(long roomId, long userId) {
        Room room = roomRepository.findById(roomId);
        if (room == null || !room.containsPlayer(userId)) {
            return;
        }
        if (room.isInGame()) {
            if (room.getGameId() != null) {
                activityTracker.touch(room.getGameId(), userId, clock.instant());
            }
            return;
        }
        // 대기 중인 방. touch는 꺼져 있으면(idle-timeout 0) 아무것도 하지 않고, 실패해도 예외를 삼킨다
        lobbyPresence.touch(roomId, userId);
    }
}
