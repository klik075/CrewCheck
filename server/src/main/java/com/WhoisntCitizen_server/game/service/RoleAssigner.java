package com.WhoisntCitizen_server.game.service;

import com.WhoisntCitizen_server.common.exception.GameRuleException;
import com.WhoisntCitizen_server.jobs.domain.RoleDefinition;
import com.WhoisntCitizen_server.jobs.service.RoleCatalog;
import org.springframework.stereotype.Component;

import java.util.ArrayList;
import java.util.Collections;
import java.util.List;
import java.util.Random;

/**
 * 2. 플레이어 역할 배정. 직업 정의는 DB(roles)에서 읽은 RoleCatalog를 사용한다.
 * 마피아(해적) = max(1, 인원/3), 경찰(선장) 1, 의사(선의) 1, 나머지 시민(선원).
 */
@Component
public class RoleAssigner {

    public static final int MIN_PLAYERS = 4;
    public static final int MAX_PLAYERS = 12;

    // roles.code 값 (V2__insert_roles.sql)
    public static final String MAFIA_ROLE = "PIRATE_RAIDER";
    public static final String POLICE_ROLE = "CREW_CAPTAIN";
    public static final String DOCTOR_ROLE = "CREW_DOCTOR";
    public static final String CITIZEN_ROLE = "CREW_SAILOR";

    private final Random random;
    private final RoleCatalog roleCatalog;

    public RoleAssigner(Random random, RoleCatalog roleCatalog) {
        this.random = random;
        this.roleCatalog = roleCatalog;
    }

    /** 반환 리스트의 i번째 역할이 i번째 플레이어의 역할이다. */
    public List<RoleDefinition> assign(int playerCount) {
        if (playerCount < MIN_PLAYERS || playerCount > MAX_PLAYERS) {
            throw new GameRuleException("플레이어 수는 " + MIN_PLAYERS + "~" + MAX_PLAYERS + "명이어야 합니다.");
        }

        List<RoleDefinition> roles = new ArrayList<>();
        //수정될 부분 - 역할 분배
        int mafiaCount = Math.max(1, playerCount / 3);
        for (int i = 0; i < mafiaCount; i++) {
            roles.add(roleCatalog.get(MAFIA_ROLE));
        }

        roles.add(roleCatalog.get(POLICE_ROLE));
        roles.add(roleCatalog.get(DOCTOR_ROLE));

        RoleDefinition citizen = roleCatalog.get(CITIZEN_ROLE);
        while (roles.size() < playerCount) {
            roles.add(citizen);
        }
        Collections.shuffle(roles, random);

        return roles;
    }
}
