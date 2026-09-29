package com.sparta.unityaitestproject_server.game.service;

import com.sparta.unityaitestproject_server.common.exception.GameRuleException;
import com.sparta.unityaitestproject_server.game.entity.Role;
import org.springframework.stereotype.Component;

import java.util.ArrayList;
import java.util.Collections;
import java.util.List;
import java.util.Random;

/**
 * 2. 플레이어 역할 배정.
 * 4~7명: 마피아 1, 8~11명: 마피아 2 ... (인원/4). 경찰 1명, 5명 이상이면 의사 1명, 나머지 시민.
 */
@Component
public class RoleAssigner {

    public static final int MIN_PLAYERS = 4;
    public static final int MAX_PLAYERS = 12;

    private final Random random;

    public RoleAssigner(Random random) {
        this.random = random;
    }

    /** 반환 리스트의 i번째 역할이 i번째 플레이어의 역할이다. */
    public List<Role> assign(int playerCount) {
        if (playerCount < MIN_PLAYERS || playerCount > MAX_PLAYERS) {
            throw new GameRuleException("플레이어 수는 " + MIN_PLAYERS + "~" + MAX_PLAYERS + "명이어야 합니다.");
        }

        List<Role> roles = new ArrayList<>();
        //수정될 부분 - 역할 분배
        int mafiaCount = Math.max(1, playerCount / 3);
        for (int i = 0; i < mafiaCount; i++) {
            roles.add(Role.MAFIA);
        }

        roles.add(Role.POLICE);
        roles.add(Role.DOCTOR);

        while (roles.size() < playerCount) {
            roles.add(Role.CITIZEN);
        }
        Collections.shuffle(roles, random);

        return roles;
    }
}
