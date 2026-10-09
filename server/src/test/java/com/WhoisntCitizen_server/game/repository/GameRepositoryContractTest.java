package com.WhoisntCitizen_server.game.repository;

import com.WhoisntCitizen_server.game.entity.Game;
import com.WhoisntCitizen_server.game.entity.GameEndReason;
import com.WhoisntCitizen_server.game.entity.GamePhase;
import com.WhoisntCitizen_server.game.entity.GamePlayer;
import com.WhoisntCitizen_server.game.entity.Winner;
import com.WhoisntCitizen_server.support.TestRoles;
import org.junit.jupiter.api.BeforeEach;
import org.junit.jupiter.api.Test;

import java.time.Instant;
import java.util.List;

import static org.assertj.core.api.Assertions.assertThat;

/**
 * GameRepository 구현체라면 모두 지켜야 하는 동작. 구현체마다 이 클래스를 상속해 newRepository()만 채운다.
 *  - InMemoryGameRepositoryContractTest   (지금 운영 구현)
 *  - CopyingGameRepositoryContractTest    (테스트용, JSON 저장)
 *  - RedisGameRepository도 1-7 통합 테스트에서 이 클래스를 상속해 같은 동작을 확인한다.
 *
 * 구현에 따라 꺼낸 Game이 저장된 객체 그 자체일 수도(메모리), 복사본일 수도(JSON·Redis) 있으므로
 * 상태를 바꾸면 항상 save()한 뒤에 확인한다.
 */
public abstract class GameRepositoryContractTest {

    private static final Instant NOW = Instant.parse("2026-10-01T12:00:00Z");

    protected GameRepository repository;

    protected abstract GameRepository newRepository();

    @BeforeEach
    void setUpRepository() {
        repository = newRepository();
    }

    private static Game newGame() {
        return new Game("room-1", List.of(
                new GamePlayer(1L, "해적", TestRoles.RAIDER),
                new GamePlayer(2L, "선장", TestRoles.CAPTAIN),
                new GamePlayer(3L, "선원", TestRoles.SAILOR)), true);
    }

    private static Game startedGame() {
        Game game = newGame();
        game.changePhase(GamePhase.NIGHT, NOW.plusSeconds(30));
        return game;
    }

    // ---------- 저장·조회·삭제 ----------

    @Test
    void 저장한_게임을_id로_꺼낼_수_있다() {
        Game game = startedGame();
        repository.save(game);

        Game found = repository.findById(game.getGameId()).orElseThrow();

        assertThat(found.getGameId()).isEqualTo(game.getGameId());
        assertThat(found.getPhase()).isEqualTo(GamePhase.NIGHT);
    }

    @Test
    void 없는_id는_빈_값이다() {
        assertThat(repository.findById("없는-게임")).isEmpty();
    }

    @Test
    void 삭제한_게임은_꺼낼_수_없다() {
        Game game = startedGame();
        repository.save(game);

        repository.delete(game.getGameId());

        assertThat(repository.findById(game.getGameId())).isEmpty();
    }

    @Test
    void 없는_게임을_삭제해도_예외가_나지_않는다() {
        repository.delete("없는-게임");

        assertThat(repository.findActiveIds()).isEmpty();
    }

    // ---------- 진행 중인 게임 목록 (findActiveIds) ----------

    @Test
    void 아무것도_없으면_진행_중인_게임도_없다() {
        assertThat(repository.findActiveIds()).isEmpty();
    }

    @Test
    void 시작_전과_진행_중인_게임은_목록에_들어간다() {
        Game notStarted = newGame();
        Game started = startedGame();
        repository.save(notStarted);
        repository.save(started);

        assertThat(repository.findActiveIds())
                .containsExactlyInAnyOrder(notStarted.getGameId(), started.getGameId());
    }

    @Test
    void 같은_게임을_여러_번_저장해도_한_번만_들어간다() {
        Game game = startedGame();
        repository.save(game);
        repository.save(game);

        assertThat(repository.findActiveIds()).containsExactly(game.getGameId());
    }

    @Test
    void 끝난_게임은_목록에서_빠지지만_보관_시간_동안은_꺼낼_수_있다() {
        Game game = startedGame();
        repository.save(game);

        Game loaded = repository.findById(game.getGameId()).orElseThrow();
        loaded.end(Winner.CREW, List.of(2L, 3L));
        repository.save(loaded);

        assertThat(repository.findActiveIds()).doesNotContain(game.getGameId());
        assertThat(repository.findById(game.getGameId()).orElseThrow().isEnded()).isTrue();
    }

    @Test
    void 취소된_게임도_목록에서_빠진다() {
        Game game = startedGame();
        repository.save(game);

        Game loaded = repository.findById(game.getGameId()).orElseThrow();
        loaded.cancel(GameEndReason.CANCELLED_NO_DEATHS);
        repository.save(loaded);

        assertThat(repository.findActiveIds()).doesNotContain(game.getGameId());
    }

    @Test
    void 삭제한_게임은_목록에서_빠진다() {
        Game other = startedGame();
        Game active = startedGame();
        repository.save(other);
        repository.save(active);

        repository.delete(active.getGameId());

        assertThat(repository.findActiveIds()).doesNotContain(active.getGameId()).contains(other.getGameId());
    }

    @Test
    void 돌려준_목록을_고쳐도_저장소에는_영향이_없다() {
        Game game = startedGame();
        repository.save(game);

        List<String> ids = repository.findActiveIds();
        try {
            ids.clear();
        } catch (UnsupportedOperationException ignored) {
            // 고칠 수 없는 목록을 돌려줘도 된다
        }

        assertThat(repository.findActiveIds()).containsExactly(game.getGameId());
    }
}
