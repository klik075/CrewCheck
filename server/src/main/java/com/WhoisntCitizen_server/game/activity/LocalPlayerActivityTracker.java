package com.WhoisntCitizen_server.game.activity;

import java.time.Instant;
import java.util.Collection;
import java.util.Map;
import java.util.concurrent.ConcurrentHashMap;

/** 서버 메모리에 접속 기록을 두는 구현. (서버 1대 기준) */
public class LocalPlayerActivityTracker implements PlayerActivityTracker {

    private final ConcurrentHashMap<String, ConcurrentHashMap<Long, Instant>> seen = new ConcurrentHashMap<>();

    @Override
    public void markAllSeen(String gameId, Collection<Long> playerIds, Instant now) {
        Map<Long, Instant> game = seen.computeIfAbsent(gameId, id -> new ConcurrentHashMap<>());
        playerIds.forEach(playerId -> game.put(playerId, now));
    }

    @Override
    public void touch(String gameId, Long playerId, Instant now) {
        if (gameId == null || playerId == null) {
            return;
        }
        seen.computeIfAbsent(gameId, id -> new ConcurrentHashMap<>()).put(playerId, now);
    }

    @Override
    public Map<Long, Instant> lastSeen(String gameId) {
        Map<Long, Instant> game = seen.get(gameId);
        return game == null ? Map.of() : Map.copyOf(game);
    }

    @Override
    public void clear(String gameId) {
        seen.remove(gameId);
    }
}
