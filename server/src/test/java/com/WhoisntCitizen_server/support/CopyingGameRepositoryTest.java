package com.WhoisntCitizen_server.support;

import com.WhoisntCitizen_server.game.entity.Game;
import com.WhoisntCitizen_server.game.entity.GamePhase;
import com.WhoisntCitizen_server.game.entity.GamePlayer;
import org.junit.jupiter.api.Test;

import java.time.Instant;
import java.util.List;

import static org.assertj.core.api.Assertions.assertThat;

/** 테스트용 저장소 자체가 Redis처럼 "저장한 시점의 JSON"만 남기고, 꺼낼 때마다 새 Game을 돌려주는지 확인한다. */
class CopyingGameRepositoryTest {

    private static final Instant NOW = Instant.parse("2026-10-01T12:00:00Z");

    private final CopyingGameRepository repository = new CopyingGameRepository();

    private Game newGame() {
        Game game = new Game("room-1", List.of(
                new GamePlayer(1L, "해적", TestRoles.RAIDER),
                new GamePlayer(2L, "앵무새", TestRoles.PARROT),
                new GamePlayer(3L, "선장", TestRoles.CAPTAIN),
                new GamePlayer(4L, "선원", TestRoles.SAILOR)), true);
        game.changePhase(GamePhase.NIGHT, NOW.plusSeconds(30));
        game.recordNightAction(1L, 4L);
        game.recordNightAction(2L, 1L, NOW);   // 앵무새 접선
        game.getPlayer(4L).kill();
        game.recordDeath();
        return game;
    }

    @Test
    void 꺼낸_게임은_저장한_게임과_상태가_같지만_다른_객체다() {
        Game original = newGame();
        repository.save(original);

        Game loaded = repository.findById(original.getGameId()).orElseThrow();

        assertThat(loaded).isNotSameAs(original);
        assertThat(loaded.getPlayer(1L)).isNotSameAs(original.getPlayer(1L));
        assertThat(loaded.getNightActions()).isNotSameAs(original.getNightActions());
        assertThat(loaded).usingRecursiveComparison().isEqualTo(original);
    }

    @Test
    void save_없이_고친_내용은_다음에_꺼낼_때_사라진다() {
        Game original = newGame();
        repository.save(original);

        Game loaded = repository.findById(original.getGameId()).orElseThrow();
        loaded.changePhase(GamePhase.NIGHT_RESULT, NOW.plusSeconds(35));
        loaded.getPlayer(3L).kill();

        Game again = repository.findById(original.getGameId()).orElseThrow();
        assertThat(again.getPhase()).isEqualTo(GamePhase.NIGHT);
        assertThat(again.getPlayer(3L).isAlive()).isTrue();
    }

    @Test
    void save하면_고친_내용이_남는다() {
        Game original = newGame();
        repository.save(original);

        Game loaded = repository.findById(original.getGameId()).orElseThrow();
        loaded.getPlayer(3L).kill();
        repository.save(loaded);

        assertThat(repository.findById(original.getGameId()).orElseThrow().getPlayer(3L).isAlive()).isFalse();
    }

    @Test
    void 저장한_뒤_원본을_고쳐도_저장된_상태는_바뀌지_않는다() {
        Game original = newGame();
        repository.save(original);

        original.getPlayer(3L).kill();
        original.skipNightAction(1L);

        Game loaded = repository.findById(original.getGameId()).orElseThrow();
        assertThat(loaded.getPlayer(3L).isAlive()).isTrue();
        assertThat(loaded.getNightActions()).containsKeys(1L, 2L);
    }

    @Test
    void 플레이어_순서가_유지되고_꺼낸_게임도_고칠_수_있다() {
        Game original = newGame();
        repository.save(original);

        Game loaded = repository.findById(original.getGameId()).orElseThrow();

        assertThat(loaded.getPlayers()).extracting(GamePlayer::getPlayerId).containsExactly(1L, 2L, 3L, 4L);
        assertThat(loaded.getPlayer(2L).isContacted()).isTrue();
        loaded.recordNightAction(3L, 1L); // 복사된 컬렉션도 고칠 수 있어야 한다
        assertThat(loaded.getNightActions()).containsKeys(1L, 2L, 3L);
    }

    @Test
    void 게임은_스냅샷_JSON으로_저장된다() {
        Game original = newGame();
        repository.save(original);

        String json = repository.storedJson(original.getGameId());

        assertThat(json).contains("\"schemaVersion\":1", "\"gameId\":\"" + original.getGameId() + "\"");
        assertThat(repository.saveCount()).isEqualTo(1);
    }

    @Test
    void 삭제하면_더_이상_꺼낼_수_없다() {
        Game original = newGame();
        repository.save(original);

        repository.delete(original.getGameId());

        assertThat(repository.findById(original.getGameId())).isEmpty();
        assertThat(repository.findAll()).isEmpty();
    }
}
