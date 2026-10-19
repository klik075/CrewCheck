package com.WhoisntCitizen_server.game.lock;

import com.WhoisntCitizen_server.game.activity.LocalPlayerActivityTracker;
import com.WhoisntCitizen_server.game.entity.Game;
import com.WhoisntCitizen_server.game.entity.GamePhase;
import com.WhoisntCitizen_server.game.entity.GamePlayer;
import com.WhoisntCitizen_server.game.repository.ChangeEventGameRepository;
import com.WhoisntCitizen_server.game.repository.GameRepository;
import com.WhoisntCitizen_server.game.repository.InMemoryGameRepository;
import com.WhoisntCitizen_server.game.service.GameFlowService;
import com.WhoisntCitizen_server.game.service.GameService;
import com.WhoisntCitizen_server.game.service.RoleAssigner;
import com.WhoisntCitizen_server.jobs.domain.ActionCode;
import com.WhoisntCitizen_server.jobs.domain.Faction;
import com.WhoisntCitizen_server.jobs.domain.RoleDefinition;
import org.junit.jupiter.api.Test;
import org.springframework.context.ApplicationEventPublisher;

import java.time.Clock;
import java.time.Instant;
import java.time.ZoneOffset;
import java.util.List;
import java.util.function.Supplier;

import static org.assertj.core.api.Assertions.assertThat;
import static org.mockito.Mockito.mock;

/**
 * 보여 주기만 하는 읽기(SnapshotRead)와 방 목록의 게임 확인(keepsRoomInGame)이
 *  - 복사본을 돌려주는 저장소(Redis)에서는 게임 잠금을 잡지 않고
 *  - 같은 객체를 돌려주는 저장소(메모리)에서는 지금처럼 잠금을 잡는지 확인한다.
 */
class SnapshotReadTest {

    private static final RoleDefinition RAIDER =
            new RoleDefinition("PIRATE_RAIDER", "해적", Faction.PIRATE, ActionCode.SELECT_ATTACK_TARGET);
    private static final RoleDefinition SAILOR =
            new RoleDefinition("CREW_SAILOR", "선원", Faction.CREW, null);
    private static final Instant NOW = Instant.parse("2026-10-01T12:00:00Z");

    /** 잠금을 몇 번 잡았는지 센다. */
    private static final class CountingGameLock implements GameLock {
        private final LocalGameLock delegate = new LocalGameLock();
        int acquired;

        @Override
        public <T> T withLock(String gameId, Supplier<T> action) {
            acquired++;
            return delegate.withLock(gameId, action);
        }
    }

    /** Redis처럼 "복사본을 돌려준다"고 알리는 메모리 저장소 (잠금을 잡는지만 보면 되므로 실제 복사는 하지 않는다) */
    private static final class SnapshotGameRepository extends InMemoryGameRepository {
        @Override
        public boolean returnsSnapshots() {
            return true;
        }
    }

    private static Game newGame(GameRepository repository) {
        Game game = new Game("room-1", List.of(
                new GamePlayer(1L, "p1", RAIDER),
                new GamePlayer(2L, "p2", SAILOR),
                new GamePlayer(3L, "p3", SAILOR),
                new GamePlayer(4L, "p4", SAILOR)));
        game.changePhase(GamePhase.NIGHT, NOW.plusSeconds(30));
        repository.save(game);
        return game;
    }

    private static GameService newService(GameRepository repository, GameLock lock) {
        return new GameService(repository, mock(RoleAssigner.class), mock(GameFlowService.class),
                lock, new LocalPlayerActivityTracker(), Clock.fixed(NOW, ZoneOffset.UTC));
    }

    @Test
    void 복사본_저장소면_잠금_없이_읽는다() {
        CountingGameLock lock = new CountingGameLock();

        String result = SnapshotRead.of(new SnapshotGameRepository(), lock, "g1", () -> "ok");

        assertThat(result).isEqualTo("ok");
        assertThat(lock.acquired).isZero();
    }

    @Test
    void 메모리_저장소면_지금처럼_잠금_안에서_읽는다() {
        CountingGameLock lock = new CountingGameLock();

        String result = SnapshotRead.of(new InMemoryGameRepository(), lock, "g1", () -> "ok");

        assertThat(result).isEqualTo("ok");
        assertThat(lock.acquired).isEqualTo(1);
    }

    @Test
    void 저장_알림_저장소는_감싼_저장소를_따른다() {
        ApplicationEventPublisher publisher = mock(ApplicationEventPublisher.class);

        assertThat(new ChangeEventGameRepository(new SnapshotGameRepository(), publisher).returnsSnapshots()).isTrue();
        assertThat(new ChangeEventGameRepository(new InMemoryGameRepository(), publisher).returnsSnapshots()).isFalse();
    }

    @Test
    void 복사본_저장소면_상태_조회가_잠금을_잡지_않는다() {
        SnapshotGameRepository repository = new SnapshotGameRepository();
        CountingGameLock lock = new CountingGameLock();
        Game game = newGame(repository);

        newService(repository, lock).getState(game.getGameId(), 1L);

        assertThat(lock.acquired).isZero();
    }

    @Test
    void 방_목록_확인은_살아_있는_게임이면_잠금_없이_끝낸다() {
        SnapshotGameRepository repository = new SnapshotGameRepository();
        CountingGameLock lock = new CountingGameLock();
        Game game = newGame(repository);

        boolean keeps = newService(repository, lock).keepsRoomInGame(game.getGameId());

        assertThat(keeps).isTrue();
        assertThat(lock.acquired).isZero();
    }

    @Test
    void 방_목록_확인은_게임이_없어_보이면_잠금_안에서_다시_확인한다() {
        SnapshotGameRepository repository = new SnapshotGameRepository();
        CountingGameLock lock = new CountingGameLock();

        boolean keeps = newService(repository, lock).keepsRoomInGame("missing-game");

        assertThat(keeps).isFalse(); // 방을 대기 상태로 되돌리는 결정이라 잠금 안에서 확정한다
        assertThat(lock.acquired).isEqualTo(1);
    }

    @Test
    void 메모리_저장소면_방_목록_확인도_지금처럼_잠금을_잡는다() {
        InMemoryGameRepository repository = new InMemoryGameRepository();
        CountingGameLock lock = new CountingGameLock();
        Game game = newGame(repository);

        assertThat(newService(repository, lock).keepsRoomInGame(game.getGameId())).isTrue();
        assertThat(lock.acquired).isEqualTo(1);
    }
}
