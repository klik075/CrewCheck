package com.sparta.unityaitestproject_server.night.service;

import com.sparta.unityaitestproject_server.game.entity.Game;
import com.sparta.unityaitestproject_server.game.entity.GamePlayer;
import com.sparta.unityaitestproject_server.game.entity.Role;
import com.sparta.unityaitestproject_server.night.entity.NightResult;
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
 * - 마피아: 가장 많이 지목된 대상을 처치 (동률이면 무작위)
 * - 의사: 보호 대상이 처치 대상과 같으면 생존
 * - 경찰: 대상이 마피아인지 조사 (결과는 해당 경찰에게만 공개)
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
            Role role = actor.getRole();
            if (role == Role.MAFIA) {
                mafiaPicks.merge(target.getPlayerId(), 1, Integer::sum);
            } else if (role == Role.DOCTOR) {
                protectedIds.add(target.getPlayerId());
            } else if (role == Role.POLICE) {
                investigations.put(actor.getPlayerId(),
                        new NightResult.Investigation(target.getPlayerId(), target.isMafia()));
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
