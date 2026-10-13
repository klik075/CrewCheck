package com.WhoisntCitizen_server.game.repository;

import com.WhoisntCitizen_server.common.servermode.ConditionalOnServerSetting;
import com.WhoisntCitizen_server.common.servermode.ServerSetting;
import com.WhoisntCitizen_server.game.entity.Game;
import org.springframework.stereotype.Repository;

import java.util.List;
import java.util.Map;
import java.util.Optional;
import java.util.concurrent.ConcurrentHashMap;

/**
 * 게임 상태를 서버 메모리에 저장하는 GameRepository. 서버가 꺼지면 진행 중인 게임도 사라진다.
 * mafia.game.repository의 최종 값이 memory일 때 쓰인다. (직접 지정, 또는 비워 두고 mafia.server.mode=single) redis면 GameRedisConfig의 RedisGameRepository가 쓰인다.
 * 꺼낸 Game은 저장된 객체 그 자체라서 save()를 빼먹어도 티가 나지 않는다. 저장 규칙은 GameSaveDisciplineTest가 검사한다.
 */
@Repository
@ConditionalOnServerSetting(value = ServerSetting.GAME_REPOSITORY, havingValue = "memory")
public class InMemoryGameRepository implements GameRepository {

    private final Map<String, Game> store = new ConcurrentHashMap<>();

    @Override
    public Game save(Game game) {
        store.put(game.getGameId(), game);
        return game;
    }

    @Override
    public Optional<Game> findById(String gameId) {
        return Optional.ofNullable(store.get(gameId));
    }

    @Override
    public List<String> findActiveIds() {
        return store.values().stream()
                .filter(game -> !game.isEnded())
                .map(Game::getGameId)
                .toList();
    }

    @Override
    public void delete(String gameId) {
        store.remove(gameId);
    }
}
