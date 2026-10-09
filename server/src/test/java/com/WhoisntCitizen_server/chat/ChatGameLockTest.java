package com.WhoisntCitizen_server.chat;

import com.WhoisntCitizen_server.chat.service.ChatMessageService;
import com.WhoisntCitizen_server.common.exception.ForbiddenException;
import com.WhoisntCitizen_server.game.entity.Game;
import com.WhoisntCitizen_server.game.entity.GamePhase;
import com.WhoisntCitizen_server.game.entity.GamePlayer;
import com.WhoisntCitizen_server.game.lock.GameLock;
import com.WhoisntCitizen_server.game.lock.LocalGameLock;
import com.WhoisntCitizen_server.game.repository.GameRepository;
import com.WhoisntCitizen_server.lobby.domain.room.Room;
import com.WhoisntCitizen_server.lobby.domain.room.RoomPlayer;
import com.WhoisntCitizen_server.support.CopyingGameRepository;
import com.WhoisntCitizen_server.support.TestRoles;
import com.WhoisntCitizen_server.user.entity.User;
import com.WhoisntCitizen_server.user.repository.UserRepository;
import org.junit.jupiter.api.BeforeEach;
import org.junit.jupiter.api.Test;

import java.time.Instant;
import java.util.List;
import java.util.Optional;
import java.util.function.Supplier;

import static org.assertj.core.api.Assertions.assertThat;
import static org.assertj.core.api.Assertions.assertThatThrownBy;
import static org.mockito.Mockito.mock;
import static org.mockito.Mockito.when;

/**
 * 채팅이 게임을 "게임 잠금 안에서" 읽는지 확인한다.
 *
 * Redis 저장소는 읽을 때마다 복사본을 만들기 때문에, 잠금 밖에서 읽은 게임으로 판단하면
 * 그사이 밤이 시작돼도 알 수 없다. 그래서
 *  - 저장소 조회(findById)가 모두 잠금 안에서 일어나는지
 *  - 전송·조회 한 번에 잠금을 한 번만 잡는지
 *  - Redis처럼 복사본을 돌려주는 저장소(CopyingGameRepository)에서도 규칙이 맞게 적용되는지
 * 를 확인한다.
 */
class ChatGameLockTest {

    private static final long ROOM_ID = 1L;
    private static final long PIRATE_MEMBER = 10L, PIRATE_USER = 1L;
    private static final long SAILOR_MEMBER = 20L, SAILOR_USER = 2L;

    private CountingGameLock lock;
    private LockCheckingGameRepository games;
    private ChatMessageService service;
    private Room room;

    /** 잠금을 몇 번 잡았는지, 지금 잠금 안인지 기록한다. */
    private static final class CountingGameLock implements GameLock {
        private final LocalGameLock delegate = new LocalGameLock();
        private final ThreadLocal<Integer> depth = ThreadLocal.withInitial(() -> 0);
        int acquired;

        @Override
        public <T> T withLock(String gameId, Supplier<T> action) {
            acquired++;
            return delegate.withLock(gameId, () -> {
                depth.set(depth.get() + 1);
                try {
                    return action.get();
                } finally {
                    depth.set(depth.get() - 1);
                }
            });
        }

        boolean inLock() {
            return depth.get() > 0;
        }
    }

    /** Redis처럼 복사본을 돌려주고, 잠금 밖에서 읽으면 기록한다. */
    private static final class LockCheckingGameRepository implements GameRepository {
        private final CopyingGameRepository delegate = new CopyingGameRepository();
        private final CountingGameLock lock;
        int readsOutsideLock;

        LockCheckingGameRepository(CountingGameLock lock) {
            this.lock = lock;
        }

        @Override
        public Game save(Game game) {
            return delegate.save(game);
        }

        @Override
        public Optional<Game> findById(String gameId) {
            if (!lock.inLock()) {
                readsOutsideLock++;
            }
            return delegate.findById(gameId);
        }

        @Override
        public List<String> findActiveIds() {
            return delegate.findActiveIds();
        }

        @Override
        public void delete(String gameId) {
            delegate.delete(gameId);
        }
    }

    @BeforeEach
    void setUp() {
        lock = new CountingGameLock();
        games = new LockCheckingGameRepository(lock);

        InMemoryLobbyRoomRepository rooms = new InMemoryLobbyRoomRepository();
        room = new Room(ROOM_ID, "1번방", PIRATE_USER, 8);
        room.addPlayer(new RoomPlayer(PIRATE_USER, "해적", false));
        room.addPlayer(new RoomPlayer(SAILOR_USER, "선원", false));
        rooms.save(room);

        UserRepository users = mock(UserRepository.class);
        when(users.findByMemberId(PIRATE_MEMBER))
                .thenReturn(Optional.of(User.builder().id(PIRATE_USER).nickname("해적").build()));
        when(users.findByMemberId(SAILOR_MEMBER))
                .thenReturn(Optional.of(User.builder().id(SAILOR_USER).nickname("선원").build()));

        service = new ChatMessageService(new InMemoryChatMessageRepository(), rooms, users, games, lock);
    }

    /** 게임을 시작해 저장한다. 이후 상태를 바꾸려면 저장소에서 다시 꺼내 바꾸고 save한다. (Redis와 같은 방식) */
    private String startGame(GamePhase phase) {
        Game game = new Game(String.valueOf(ROOM_ID), List.of(
                new GamePlayer(PIRATE_USER, "해적", TestRoles.RAIDER),
                new GamePlayer(SAILOR_USER, "선원", TestRoles.SAILOR)));
        game.changePhase(phase, Instant.now().plusSeconds(60));
        games.save(game);
        room.startGame(game.getGameId());
        return game.getGameId();
    }

    private void changePhase(String gameId, GamePhase phase) {
        Game game = games.findById(gameId).orElseThrow();
        game.changePhase(phase, Instant.now().plusSeconds(60));
        games.save(game);
    }

    @Test
    void 전송할_때_게임을_잠금_안에서_한_번만_읽는다() {
        startGame(GamePhase.DAY);
        lock.acquired = 0;
        games.readsOutsideLock = 0;

        service.send(ROOM_ID, SAILOR_MEMBER, "안녕");

        assertThat(games.readsOutsideLock).isZero();
        assertThat(lock.acquired).isEqualTo(1);
    }

    @Test
    void 조회할_때_게임을_잠금_안에서_한_번만_읽는다() {
        String gameId = startGame(GamePhase.NIGHT);
        service.send(ROOM_ID, PIRATE_MEMBER, "오늘 밤은 선장"); // 해적만 보이는 메시지 → 조회 때 게임 확인이 필요
        changePhase(gameId, GamePhase.NIGHT_RESULT);
        lock.acquired = 0;
        games.readsOutsideLock = 0;

        service.getMessages(ROOM_ID, SAILOR_MEMBER, null, null);

        assertThat(games.readsOutsideLock).isZero();
        assertThat(lock.acquired).isEqualTo(1);
    }

    @Test
    void 저장된_페이즈가_바뀌면_바로_다음_전송부터_새_규칙이_적용된다() {
        String gameId = startGame(GamePhase.DAY);
        service.send(ROOM_ID, SAILOR_MEMBER, "낮에는 됨");

        changePhase(gameId, GamePhase.NIGHT);               // 다른 스레드(타이머)가 밤으로 바꿔 저장한 상황

        assertThatThrownBy(() -> service.send(ROOM_ID, SAILOR_MEMBER, "밤에는 안 됨"))
                .isInstanceOf(ForbiddenException.class);
    }

    @Test
    void 게임_중이_아니면_잠금을_잡지_않는다() {
        service.send(ROOM_ID, SAILOR_MEMBER, "대기실 채팅");
        service.getMessages(ROOM_ID, SAILOR_MEMBER, null, null);

        assertThat(lock.acquired).isZero();
    }
}
