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
        if (gameId == null || playerIds == null) {
            return;
        }
        playerIds.forEach(playerId -> touch(gameId, playerId, now));
    }

    @Override
    public void touch(String gameId, Long playerId, Instant now) {
        if (gameId == null || playerId == null || now == null) {
            return;
        }
        // merge는 키 단위로 원자적이라, 동시에 기록해도 더 늦은 시각이 남는다
        seen.computeIfAbsent(gameId, id -> new ConcurrentHashMap<>())
                .merge(playerId, now, LocalPlayerActivityTracker::later);
    }

    @Override
    public Map<Long, Instant> lastSeen(String gameId) {
        if (gameId == null) {
            return Map.of();
        }
        Map<Long, Instant> game = seen.get(gameId);
        return game == null ? Map.of() : Map.copyOf(game);
    }

    @Override
    public void clear(String gameId) {
        if (gameId != null) {
            seen.remove(gameId);
        }
    }

    private static Instant later(Instant current, Instant incoming) {
        return incoming.isAfter(current) ? incoming : current;
    }
}
