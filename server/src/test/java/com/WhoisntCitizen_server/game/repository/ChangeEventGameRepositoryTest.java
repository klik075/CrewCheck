package com.WhoisntCitizen_server.game.repository;

import com.WhoisntCitizen_server.game.entity.Game;
import com.WhoisntCitizen_server.game.entity.GamePlayer;
import com.WhoisntCitizen_server.game.event.GameStateChangedEvent;
import com.WhoisntCitizen_server.game.lock.LocalGameLock;
import com.WhoisntCitizen_server.support.TestRoles;
import org.junit.jupiter.api.Test;

import java.util.ArrayList;
import java.util.Collections;
import java.util.List;

import static org.assertj.core.api.Assertions.assertThat;
import static org.assertj.core.api.Assertions.assertThatThrownBy;

/**
 * 저장할 때 GameStateChangedEvent가 언제, 몇 번 발행되는지 확인한다.
 * 게임 잠금은 실제 LocalGameLock을 쓴다. (잠금 구현이 GameLockScope.enter/exit를 부른다)
 */
class ChangeEventGameRepositoryTest {

    private final List<Object> events = Collections.synchronizedList(new ArrayList<>());
    private final InMemoryGameRepository store = new InMemoryGameRepository();
    private final ChangeEventGameRepository repository = new ChangeEventGameRepository(store, events::add);
    private final LocalGameLock lock = new LocalGameLock();

    private static Game game(String roomId) {
        return new Game(roomId, List.of(
                new GamePlayer(1L, "해적", TestRoles.RAIDER),
                new GamePlayer(2L, "선장", TestRoles.CAPTAIN),
                new GamePlayer(3L, "선원3", TestRoles.SAILOR),
                new GamePlayer(4L, "선원4", TestRoles.SAILOR)), true);
    }

    @Test
    void 저장은_감싼_저장소에_그대로_된다() {
        Game game = repository.save(game("3"));

        assertThat(store.findById(game.getGameId())).containsSame(game);
        assertThat(repository.findById(game.getGameId())).containsSame(game);
    }

    @Test
    void 잠금_밖에서_저장하면_바로_발행한다() {
        Game game = repository.save(game("3"));

        assertThat(events).containsExactly(new GameStateChangedEvent(game.getGameId(), "3", game.getPhaseVersion()));
    }

    @Test
    void 잠금_안에서_저장하면_잠금이_풀린_뒤에_발행한다() {
        Game game = store.save(game("3"));
        List<Object> seenInsideLock = new ArrayList<>();

        lock.runWithLock(game.getGameId(), () -> {
            repository.save(game);
            seenInsideLock.addAll(events);
        });

        assertThat(seenInsideLock).as("잠금 안에서는 아직 발행하지 않는다").isEmpty();
        assertThat(events).hasSize(1);
    }

    @Test
    void 같은_잠금_안에서_여러_번_저장해도_한_번만_발행한다() {
        Game game = store.save(game("3"));

        lock.runWithLock(game.getGameId(), () -> {
            repository.save(game);
            repository.save(game);
            repository.save(game);
        });

        assertThat(events).hasSize(1);
    }

    @Test
    void 잠금이_겹쳐도_가장_바깥_잠금이_풀린_뒤에_한_번만_발행한다() {
        Game game = store.save(game("3"));

        lock.runWithLock(game.getGameId(), () -> {
            repository.save(game);
            lock.runWithLock(game.getGameId(), () -> repository.save(game));
            assertThat(events).isEmpty();
        });

        assertThat(events).hasSize(1);
    }

    @Test
    void 한_잠금_안에서_다른_게임도_저장하면_게임마다_발행한다() {
        Game first = store.save(game("3"));
        Game second = store.save(game("4"));

        lock.runWithLock(first.getGameId(), () -> {
            repository.save(first);
            repository.save(second);
        });

        assertThat(events).extracting(e -> ((GameStateChangedEvent) e).gameId())
                .containsExactly(first.getGameId(), second.getGameId());
    }

    @Test
    void 작업이_예외로_끝나도_이미_저장한_변경은_발행한다() {
        Game game = store.save(game("3"));

        assertThatThrownBy(() -> lock.runWithLock(game.getGameId(), () -> {
            repository.save(game);
            throw new IllegalStateException("판정 오류 (테스트)");
        })).isInstanceOf(IllegalStateException.class);

        assertThat(events).hasSize(1);
    }

    @Test
    void 다음_잠금에서는_새로_모은다() {
        Game game = store.save(game("3"));

        lock.runWithLock(game.getGameId(), () -> repository.save(game));
        lock.runWithLock(game.getGameId(), () -> repository.save(game));

        assertThat(events).hasSize(2);
    }

    @Test
    void 삭제는_발행하지_않는다() {
        Game game = store.save(game("3"));

        repository.delete(game.getGameId());

        assertThat(events).isEmpty();
        assertThat(store.findById(game.getGameId())).isEmpty();
    }
}
