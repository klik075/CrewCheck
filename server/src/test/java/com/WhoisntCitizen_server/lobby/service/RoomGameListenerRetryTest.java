package com.WhoisntCitizen_server.lobby.service;

import com.WhoisntCitizen_server.chat.InMemoryLobbyRoomRepository;
import com.WhoisntCitizen_server.common.event.RoomNoticeEvent;
import com.WhoisntCitizen_server.common.exception.LockTimeoutException;
import com.WhoisntCitizen_server.common.lock.LockBusyRetry;
import com.WhoisntCitizen_server.game.entity.GameEndReason;
import com.WhoisntCitizen_server.game.entity.Winner;
import com.WhoisntCitizen_server.game.event.CancelledGameExpiredEvent;
import com.WhoisntCitizen_server.game.event.GameEndedEvent;
import com.WhoisntCitizen_server.game.event.PlayersDepartedEvent;
import com.WhoisntCitizen_server.game.service.GameService;
import com.WhoisntCitizen_server.lobby.domain.room.Room;
import com.WhoisntCitizen_server.lobby.domain.room.RoomPlayer;
import com.WhoisntCitizen_server.lobby.event.RoomDeletedEvent;
import com.WhoisntCitizen_server.lobby.lock.LocalRoomLock;
import com.WhoisntCitizen_server.lobby.lock.RoomLock;
import com.WhoisntCitizen_server.support.ManualTaskScheduler;
import com.WhoisntCitizen_server.support.MutableClock;
import com.WhoisntCitizen_server.user.repository.UserRepository;
import org.junit.jupiter.api.BeforeEach;
import org.junit.jupiter.api.Test;

import java.time.Duration;
import java.time.Instant;
import java.util.ArrayList;
import java.util.List;
import java.util.concurrent.atomic.AtomicInteger;
import java.util.function.Supplier;

import static org.assertj.core.api.Assertions.assertThat;
import static org.mockito.Mockito.mock;

/**
 * 2-8: 게임 이벤트를 받은 로비 리스너가 방 잠금을 못 잡았을 때, 이벤트를 다시 발행하지 않고
 * 그 리스너의 일만 1초 뒤 다시 시도하는지 확인한다. (전적 저장이 두 번 실행되지 않도록)
 */
class RoomGameListenerRetryTest {

    private static final Instant NOW = Instant.parse("2026-10-10T12:00:00Z");
    private static final long ROOM_ID = 1L;
    private static final String GAME_ID = "game-1";

    /** 남은 실패 횟수만큼 LockTimeoutException을 던지고, 그다음부터는 정상 잠금 (다른 서버가 방 잠금을 오래 쥔 상황) */
    private static final class FailingRoomLock implements RoomLock {
        private final LocalRoomLock delegate = new LocalRoomLock();
        private final AtomicInteger failuresLeft = new AtomicInteger();

        @Override
        public <T> T withLock(Long roomId, Supplier<T> action) {
            if (failuresLeft.getAndUpdate(n -> Math.max(0, n - 1)) > 0) {
                throw new LockTimeoutException("방", roomId, Duration.ofSeconds(10));
            }
            return delegate.withLock(roomId, action);
        }
    }

    private final MutableClock clock = new MutableClock(NOW);
    private final ManualTaskScheduler scheduler = new ManualTaskScheduler(clock);
    private final List<Object> events = new ArrayList<>();
    private InMemoryLobbyRoomRepository rooms;
    private FailingRoomLock roomLock;
    private RoomGameListener listener;

    @BeforeEach
    void setUp() {
        rooms = new InMemoryLobbyRoomRepository();
        roomLock = new FailingRoomLock();
        RoomService roomService = new RoomService(rooms, mock(UserRepository.class), roomLock,
                mock(GameService.class), events::add);
        listener = new RoomGameListener(roomService, events::add, new LockBusyRetry(scheduler, clock));

        Room room = new Room(ROOM_ID, "방", 10L, 8);
        room.addPlayer(new RoomPlayer(10L, "방장", false));
        room.addPlayer(new RoomPlayer(20L, "철수", false));
        room.startGame(GAME_ID);
        rooms.save(room);
    }

    private void advanceOneSecond() {
        scheduler.advance(Duration.ofSeconds(1));
        scheduler.runDue();
    }

    private static GameEndedEvent ended(GameEndReason reason, Winner winner) {
        return new GameEndedEvent(GAME_ID, String.valueOf(ROOM_ID), winner, reason, 3, true, List.of());
    }

    @Test
    void 게임_종료_후_방_복귀가_잠금을_못_잡으면_1초_뒤_다시_시도해_복귀한다() {
        roomLock.failuresLeft.set(1);

        listener.onGameEnded(ended(GameEndReason.WIN, Winner.CREW));
        assertThat(rooms.findById(ROOM_ID).isInGame()).isTrue();          // 첫 시도 실패

        advanceOneSecond();

        assertThat(rooms.findById(ROOM_ID).isInGame()).isFalse();         // 다시 시도해 복귀
        assertThat(events).filteredOn(RoomNoticeEvent.class::isInstance).hasSize(1); // 안내는 성공했을 때 한 번만
    }

    @Test
    void 연결이_끊긴_플레이어_제외가_잠금을_못_잡으면_다시_시도한다() {
        roomLock.failuresLeft.set(2);

        listener.onPlayersDeparted(new PlayersDepartedEvent(GAME_ID, String.valueOf(ROOM_ID), List.of(20L)));
        advanceOneSecond();
        assertThat(rooms.findById(ROOM_ID).containsPlayer(20L)).isTrue();  // 2번째도 실패
        advanceOneSecond();

        assertThat(rooms.findById(ROOM_ID).containsPlayer(20L)).isFalse(); // 3번째에 성공
    }

    @Test
    void 취소된_게임의_방_삭제가_잠금을_못_잡으면_다시_시도한다() {
        roomLock.failuresLeft.set(1);

        listener.onCancelledGameExpired(new CancelledGameExpiredEvent(GAME_ID, String.valueOf(ROOM_ID)));
        assertThat(rooms.findById(ROOM_ID)).isNotNull();
        advanceOneSecond();

        assertThat(rooms.findById(ROOM_ID)).isNull();
        assertThat(events).filteredOn(RoomDeletedEvent.class::isInstance).hasSize(1);
    }

    @Test
    void 끝내_잠금을_못_잡으면_포기하고_방은_조회할_때_복구에_맡긴다() {
        roomLock.failuresLeft.set(100);

        listener.onGameEnded(ended(GameEndReason.WIN, Winner.CREW));
        for (int i = 0; i < 5; i++) {
            advanceOneSecond();
        }

        // 포기한 뒤에는 방이 IN_GAME으로 남는다. 방을 조회할 때 recoverIfOrphaned가 게임이 끝난 것을 보고 되돌린다.
        assertThat(rooms.findById(ROOM_ID).isInGame()).isTrue();
        assertThat(roomLock.failuresLeft.get()).as("처음 포함 3번만 시도한다").isEqualTo(97);
    }
}
