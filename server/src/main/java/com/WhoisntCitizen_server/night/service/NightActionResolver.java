package com.WhoisntCitizen_server.night.service;

import com.WhoisntCitizen_server.game.entity.Game;
import com.WhoisntCitizen_server.game.entity.GamePlayer;
import com.WhoisntCitizen_server.jobs.domain.ActionCode;
import com.WhoisntCitizen_server.night.entity.NightResult;
import org.springframework.stereotype.Component;

import java.util.HashMap;
import java.util.HashSet;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;
import java.util.Random;
import java.util.Set;

/**
 * 3~4. 밤 능력 처리.
 * ActionCode 기준으로 처리한다.
 * - SELECT_ATTACK_TARGET(해적): 가장 많이 지목된 대상을 처치 (동률이면 무작위)
 * - PROTECT(선의): 보호 대상이 처치 대상과 같으면 생존
 * - INVESTIGATE_FACTION(선장): 대상이 해적인지 조사 (결과는 조사한 본인에게만 공개)
 */
@Component
public class NightActionResolver {

    private final Random random;

    public NightActionResolver(Random random) {
        this.random = random;
    }

    //밤 Actions 처리 및 결과 반환
    public NightResult resolve(Game game) {
        Map<Long, Long> actions = game.getNightActions();
        Map<Long, Integer> mafiaPicks = new LinkedHashMap<>();
        Set<Long> protectedIds = new HashSet<>();
        Map<Long, NightResult.Investigation> investigations = new HashMap<>();

        for (Map.Entry<Long, Long> e : actions.entrySet()) {
            GamePlayer actor = game.getPlayer(e.getKey());
            GamePlayer target = game.getPlayer(e.getValue());
            if (!actor.isAlive()) {
                continue;
            }
            ActionCode code = actor.getRole().actionCode();
            if (code == null) {
                continue;
            }
            switch (code) {
                case SELECT_ATTACK_TARGET -> mafiaPicks.merge(target.getPlayerId(), 1, Integer::sum);
                case PROTECT -> protectedIds.add(target.getPlayerId());
                case INVESTIGATE_FACTION -> investigations.put(actor.getPlayerId(),
                        new NightResult.Investigation(target.getPlayerId(), target.isPirate()));
                default -> { } // 아직 게임 로직에 연결되지 않은 능력은 무시
            }
        }

        Long killTargetId = pickMostVoted(mafiaPicks);
        Long killedId = null;
        boolean saved = false;
        if (killTargetId != null) {
            if (protectedIds.contains(killTargetId)) {
                saved = true;
            } else {
                //사망 처리
                game.getPlayer(killTargetId).kill();
                killedId = killTargetId;
            }
        }
        //Actions 응답에 조사 결과를 포함할지는 추후에 결정
        return new NightResult(game.getDay(), killedId, saved, Map.copyOf(investigations));
    }

    //최다 득표 수 id 반환, 동률은 랜덤
    private Long pickMostVoted(Map<Long, Integer> picks) {
        if (picks.isEmpty()) {
            return null;
        }
        int max = picks.values().stream().max(Integer::compare).orElse(0);
        List<Long> top = picks.entrySet().stream()
                .filter(e -> e.getValue() == max)
                .map(Map.Entry::getKey)
                .toList();
        return top.get(random.nextInt(top.size()));
    }
}
