package com.WhoisntCitizen_server.game.service;

import com.WhoisntCitizen_server.common.exception.GameRuleException;
import com.WhoisntCitizen_server.jobs.domain.RoleDefinition;
import com.WhoisntCitizen_server.jobs.service.RoleCatalog;
import org.springframework.stereotype.Component;

import java.util.ArrayList;
import java.util.Collections;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;
import java.util.Random;
import java.util.stream.IntStream;

/**
 * 2. 플레이어 역할 배정. 직업 정의는 DB(roles)에서 읽은 RoleCatalog를 사용한다.
 * 인원수별 직업 구성은 COMPOSITIONS 표를 따른다. 해적 진영은 max(1, 인원/3)명이다.
 * 구성표는 서버 시작 시 한 번 검증한다(인원수, DB에 있는 직업인지, 공격할 수 있는 해적이 있는지).
 */
@Component
public class RoleAssigner {

    public static final int MIN_PLAYERS = 4;
    public static final int MAX_PLAYERS = 12;

    // roles.code 값 (V2__insert_roles.sql, V4__add_roles.sql)
    public static final String MAFIA_ROLE = "PIRATE_RAIDER";
    public static final String PARROT_ROLE = "PIRATE_PARROT";
    public static final String POLICE_ROLE = "CREW_CAPTAIN";
    public static final String DOCTOR_ROLE = "CREW_DOCTOR";
    public static final String CITIZEN_ROLE = "CREW_SAILOR";
    public static final String LOOKOUT_ROLE = "CREW_LOOKOUT";
    public static final String BOATSWAIN_ROLE = "CREW_BOATSWAIN";
    public static final String DRUNK_ROLE = "CREW_DRUNK";
    public static final String MONKEY_ROLE = "CREW_MONKEY";

    // 원숭이의 위장 후보. 원숭이는 자신이 원숭이인 줄 모르고 이 중 하나로 보인다.
    public static final List<String> MONKEY_DISGUISE_ROLES =
            List.of(POLICE_ROLE, DOCTOR_ROLE, LOOKOUT_ROLE, BOATSWAIN_ROLE, DRUNK_ROLE);

    // 9인 이상 구성의 공통 직업(해적 2 + 앵무새 + 특수 선원 6)
    private static final List<String> NINE = List.of(
            MAFIA_ROLE, MAFIA_ROLE, PARROT_ROLE,
            POLICE_ROLE, DOCTOR_ROLE, LOOKOUT_ROLE, BOATSWAIN_ROLE, MONKEY_ROLE, DRUNK_ROLE);

    /**
     * 인원수별 직업 구성. 4~6인은 기존 구성(해적·선장·선의·선원)을 유지해 Postman 시나리오와 맞춘다.
     * 밸런스를 바꿀 때는 이 표만 고치면 되고, 잘못된 표는 서버 시작 시 거부된다.
     */
    static final Map<Integer, List<String>> COMPOSITIONS = Map.ofEntries(
            Map.entry(4, List.of(MAFIA_ROLE, POLICE_ROLE, DOCTOR_ROLE, CITIZEN_ROLE)),
            Map.entry(5, List.of(MAFIA_ROLE, POLICE_ROLE, DOCTOR_ROLE, CITIZEN_ROLE, CITIZEN_ROLE)),
            Map.entry(6, List.of(MAFIA_ROLE, MAFIA_ROLE, POLICE_ROLE, DOCTOR_ROLE, CITIZEN_ROLE, CITIZEN_ROLE)),
            Map.entry(7, List.of(MAFIA_ROLE, MAFIA_ROLE,
                    POLICE_ROLE, DOCTOR_ROLE, LOOKOUT_ROLE, MONKEY_ROLE, CITIZEN_ROLE)),
            Map.entry(8, List.of(MAFIA_ROLE, MAFIA_ROLE,
                    POLICE_ROLE, DOCTOR_ROLE, LOOKOUT_ROLE, BOATSWAIN_ROLE, MONKEY_ROLE, CITIZEN_ROLE)),
            Map.entry(9, NINE),
            Map.entry(10, plus(NINE, CITIZEN_ROLE)),
            Map.entry(11, plus(NINE, CITIZEN_ROLE, CITIZEN_ROLE)),
            Map.entry(12, plus(NINE, MAFIA_ROLE, CITIZEN_ROLE, CITIZEN_ROLE)));

    private final Random random;
    private final List<RoleDefinition> monkeyDisguises;
    private final Map<Integer, List<RoleDefinition>> compositions;

    public RoleAssigner(Random random, RoleCatalog roleCatalog) {
        this.random = random;
        // 서버 시작 시 위장 후보와 구성표의 직업이 DB에 모두 있는지 확인된다(없으면 RoleCatalog.get이 예외).
        this.monkeyDisguises = MONKEY_DISGUISE_ROLES.stream().map(roleCatalog::get).toList();
        this.compositions = resolve(roleCatalog);
    }

    /**
     * 본인에게 보일 직업. 원숭이면 위장 후보 중 무작위로 정하고, 아니면 실제 직업 그대로.
     * 배정할 때 한 번만 호출해 GamePlayer에 고정한다.
     */
    public RoleDefinition shownRoleOf(RoleDefinition role) {
        if (!role.isMonkey()) {
            return role;
        }
        return monkeyDisguises.get(random.nextInt(monkeyDisguises.size()));
    }

    /** 반환 리스트의 i번째 역할이 i번째 플레이어의 역할이다. */
    public List<RoleDefinition> assign(int playerCount) {
        List<RoleDefinition> composition = compositions.get(playerCount);
        if (composition == null) {
            throw new GameRuleException("플레이어 수는 " + MIN_PLAYERS + "~" + MAX_PLAYERS + "명이어야 합니다.");
        }
        List<RoleDefinition> roles = new ArrayList<>(composition);
        Collections.shuffle(roles, random);
        return roles;
    }

    /** 구성표의 코드를 RoleDefinition으로 바꾸면서 표가 규칙에 맞는지 검증한다. */
    private static Map<Integer, List<RoleDefinition>> resolve(RoleCatalog roleCatalog) {
        Map<Integer, List<RoleDefinition>> resolved = new LinkedHashMap<>();
        IntStream.rangeClosed(MIN_PLAYERS, MAX_PLAYERS).forEach(count -> {
            List<String> codes = COMPOSITIONS.get(count);
            if (codes == null || codes.size() != count) {
                throw new IllegalStateException(count + "인 직업 구성이 없거나 인원수와 맞지 않습니다: " + codes);
            }
            List<RoleDefinition> roles = codes.stream().map(roleCatalog::get).toList();
            if (roles.stream().noneMatch(RoleDefinition::isRaider)) {
                throw new IllegalStateException(count + "인 구성에 공격할 수 있는 해적이 없습니다.");
            }
            resolved.put(count, roles);
        });
        return Map.copyOf(resolved);
    }

    private static List<String> plus(List<String> base, String... more) {
        List<String> list = new ArrayList<>(base);
        list.addAll(List.of(more));
        return List.copyOf(list);
    }
}