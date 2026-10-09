package com.WhoisntCitizen_server.game.repository;

import com.WhoisntCitizen_server.game.entity.Game;
import org.springframework.stereotype.Repository;

import java.util.List;
import java.util.Map;
import java.util.Optional;
import java.util.concurrent.ConcurrentHashMap;

@Repository
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
