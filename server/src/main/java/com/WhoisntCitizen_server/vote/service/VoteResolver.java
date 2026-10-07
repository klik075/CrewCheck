package com.WhoisntCitizen_server.vote.service;

import com.WhoisntCitizen_server.game.entity.DeathCause;
import com.WhoisntCitizen_server.game.entity.Game;
import com.WhoisntCitizen_server.vote.entity.ExecutionResult;
import org.springframework.stereotype.Component;

import java.util.LinkedHashMap;
import java.util.Map;

/**
 * 7. 처형 처리. 최다 득표자 1명을 처형하고, 동률이거나 아무도 투표하지 않았으면 처형하지 않는다.
 */
@Component
public class VoteResolver {

    public ExecutionResult resolve(Game game) {
        Map<Long, Integer> counts = new LinkedHashMap<>();
        game.getVotes().forEach((voterId, targetId) -> {
            if (game.getPlayer(voterId).isAlive()) {
                counts.merge(targetId, 1, Integer::sum);
            }
        });

        if (counts.isEmpty()) {
            return new ExecutionResult(game.getDay(), null, false, Map.of());
        }

        int max = counts.values().stream().max(Integer::compare).orElse(0);
        var top = counts.entrySet().stream().filter(e -> e.getValue() == max).toList();
        if (top.size() > 1) {
            return new ExecutionResult(game.getDay(), null, true, Map.copyOf(counts));
        }

        Long executedId = top.get(0).getKey();
        game.getPlayer(executedId).kill(DeathCause.EXECUTION);
        return new ExecutionResult(game.getDay(), executedId, false, Map.copyOf(counts));
    }
}
