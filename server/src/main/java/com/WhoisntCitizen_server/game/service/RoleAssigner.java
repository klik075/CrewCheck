package com.WhoisntCitizen_server.game.service;

import com.WhoisntCitizen_server.common.exception.GameRuleException;
import com.WhoisntCitizen_server.game.dto.RoleComposition;
import com.WhoisntCitizen_server.game.dto.RoleSetup;
import com.WhoisntCitizen_server.game.dto.RoleSetupMode;
import com.WhoisntCitizen_server.jobs.domain.Faction;
import com.WhoisntCitizen_server.jobs.domain.RoleDefinition;
import com.WhoisntCitizen_server.jobs.service.RoleCatalog;
import org.springframework.stereotype.Component;

import java.util.ArrayList;
import java.util.Collections;
import java.util.Comparator;
import java.util.HashSet;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;
import java.util.Random;
import java.util.Set;
import java.util.TreeMap;
import java.util.stream.IntStream;

/**
 * 2. 플레이어 역할 배정. 직업 정의는 DB(roles)에서 읽은 RoleCatalog를 사용한다.
 * 방의 직업 배정 설정(RoleSetup)에 따라 세 가지 방식으로 구성을 만든다.
 *  - RECOMMENDED: 인원수별 추천 구성표(RECOMMENDED). 해적 진영은 max(1, 인원/3)명이다.
 *  - CUSTOM: 방장이 편집한 인원수별 구성표. 편집하지 않은 인원수는 추천 구성을 쓴다.
 *  - RANDOM: 진영 수는 추천 구성과 같고, 각 진영 안의 직업을 후보 중에서 무작위로 뽑는다. 제3 세력은 후보가 될 수 없다.
 * 어떤 방식이든 인원수와 개수가 같고, 공격할 수 있는 해적이 있고, 해적 진영이 선원 진영보다 적어야 하고,
 * 원숭이가 있으면 원숭이가 위장할 직업도 1개 이상 있어야 한다.
 * 추천 구성표는 서버 시작 시, 커스텀 구성은 방장이 저장할 때와 게임 시작 때 이 규칙으로 검증한다.
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

    // 9인 이상 구성의 공통 직업(해적 2 + 앵무새 + 특수 선원 6)
    private static final List<String> NINE = List.of(
            MAFIA_ROLE, MAFIA_ROLE, PARROT_ROLE,
            POLICE_ROLE, DOCTOR_ROLE, LOOKOUT_ROLE, BOATSWAIN_ROLE, MONKEY_ROLE, DRUNK_ROLE);

    /**
     * 인원수별 추천 구성. 4~6인은 기존 구성(해적·선장·선의·선원)을 유지해 Postman 시나리오와 맞춘다.
     * 방의 기본 설정(RECOMMENDED)이 이 표를 쓰고, RANDOM의 진영 수와 CUSTOM 편집의 시작값도 이 표에서 나온다.
     * 밸런스를 바꿀 때는 이 표만 고치면 되고, 잘못된 표는 서버 시작 시 거부된다.
     */
    static final Map<Integer, List<String>> RECOMMENDED = Map.ofEntries(
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
    private final RoleCatalog roleCatalog;
    private final RoleDefinition raider;
    private final RoleDefinition sailor;
    private final Map<Integer, List<RoleDefinition>> recommended;
    // 고를 수 있는 직업 전체와 그중 랜덤 후보가 될 수 있는 특수 직업(해적·선원 제외). 둘 다 화면 표시 순서다.
    private final List<RoleDefinition> roles;
    private final List<String> specialRoleCodes;

    public RoleAssigner(Random random, RoleCatalog roleCatalog) {
        this.random = random;
        this.roleCatalog = roleCatalog;
        // 서버 시작 시 구성표의 직업이 DB에 모두 있는지 확인된다(없으면 RoleCatalog.get이 예외).
        this.raider = roleCatalog.get(MAFIA_ROLE);
        this.sailor = roleCatalog.get(CITIZEN_ROLE);
        this.recommended = resolveRecommended(roleCatalog);
        this.roles = roleCatalog.all().stream().sorted(displayOrder()).toList();
        // 제3 세력(크라켄·세이렌 등)은 인원 구성을 정할 때까지 랜덤 후보에서 빼고 커스텀 구성에서만 고른다.
        this.specialRoleCodes = roles.stream()
                .filter(role -> !isBaseRole(role) && !role.isNeutral())
                .map(RoleDefinition::code)
                .toList();
    }

    /**
     * 본인에게 보일 직업. 원숭이면 이번 게임 구성(composition)에 있는 위장 후보 중 무작위로 정하고, 아니면 실제 직업 그대로.
     * 구성에 없는 직업으로 보이면 원숭이가 바로 눈치채므로 구성 밖에서는 뽑지 않는다. 같은 직업이 여럿이어도 한 번으로 센다.
     * 배정할 때 한 번만 호출해 GamePlayer에 고정한다.
     *
     * @param composition 이번 게임에 배정된 직업 전체 (assign의 반환값)
     */
    public RoleDefinition shownRoleOf(RoleDefinition role, List<RoleDefinition> composition) {
        if (!role.isMonkey()) {
            return role;
        }
        List<RoleDefinition> disguises = composition.stream()
                .filter(RoleAssigner::isMonkeyDisguise)
                .distinct()
                .toList();
        if (disguises.isEmpty()) {
            // 구성 규칙(problemOf)이 막으므로 오지 않는다. 랜덤 구성은 선원 진영 자리가 2개 이상이라 항상 후보가 있다.
            throw new IllegalStateException("원숭이가 위장할 직업이 구성에 없습니다: " + composition);
        }
        return disguises.get(random.nextInt(disguises.size()));
    }

    /** 원숭이가 위장할 수 있는 직업: 원숭이를 뺀 선원 진영 전부와 능력이 없는 제3 세력(유령 선장·인어). */
    static boolean isMonkeyDisguise(RoleDefinition role) {
        return (role.faction() == Faction.CREW && !role.isMonkey()) || role.isGhostCaptain() || role.isMermaid();
    }

    /** 추천 구성으로 배정한다. 반환 리스트의 i번째 역할이 i번째 플레이어의 역할이다. */
    public List<RoleDefinition> assign(int playerCount) {
        return assign(playerCount, null);
    }

    /**
     * 방의 설정대로 배정한다. setup이 null이면 추천 구성. 반환 리스트의 i번째 역할이 i번째 플레이어의 역할이다.
     * 저장된 커스텀 구성이 지금 규칙에 맞지 않으면(서버 재시작 후 직업이 빠진 경우 등) GameRuleException(409).
     */
    public List<RoleDefinition> assign(int playerCount, RoleSetup setup) {
        if (playerCount < MIN_PLAYERS || playerCount > MAX_PLAYERS) {
            throw new GameRuleException("플레이어 수는 " + MIN_PLAYERS + "~" + MAX_PLAYERS + "명이어야 합니다.");
        }
        RoleSetupMode mode = setup == null || setup.mode() == null ? RoleSetupMode.RECOMMENDED : setup.mode();
        List<RoleDefinition> assigned = switch (mode) {
            case RECOMMENDED -> new ArrayList<>(recommended.get(playerCount));
            case CUSTOM -> customComposition(playerCount, setup);
            case RANDOM -> randomComposition(playerCount, setup.randomCandidates());
        };
        Collections.shuffle(assigned, random);
        return assigned;
    }

    // ---------- 방 설정 ----------

    /** 새 방의 설정: 추천 구성. 커스텀 표는 비어 있고(전부 추천 구성), 랜덤 후보는 특수 직업 전부다. */
    public RoleSetup defaultSetup() {
        return new RoleSetup(RoleSetupMode.RECOMMENDED, List.of(), specialRoleCodes);
    }

    /**
     * 방장이 보낸 설정을 검증하고 저장할 형태로 정리한다. 규칙에 맞지 않으면 IllegalArgumentException(400).
     * 지금 고른 모드가 아니어도 저장되는 값은 모두 검증한다. (나중에 모드만 바꿔도 바로 쓸 수 있도록)
     *  - 커스텀 표: 인원수 순으로 정렬. 인원수마다 개수·직업·해적 규칙을 검사한다.
     *  - 랜덤 후보: 화면 표시 순서로 정렬하고 중복과 해적·선원(항상 후보)을 뺀다. null이면 특수 직업 전부.
     */
    public RoleSetup normalize(RoleSetup setup) {
        if (setup == null || setup.mode() == null) {
            throw new IllegalArgumentException("직업 배정 방식(mode)을 골라 주세요.");
        }

        Map<Integer, List<String>> custom = new TreeMap<>();
        if (setup.customCompositions() != null) {
            for (RoleComposition composition : setup.customCompositions()) {
                if (composition == null) {
                    throw new IllegalArgumentException("비어 있는 커스텀 구성이 있습니다.");
                }
                int playerCount = composition.playerCount();
                if (playerCount < MIN_PLAYERS || playerCount > MAX_PLAYERS) {
                    throw new IllegalArgumentException(
                            "커스텀 구성의 인원수는 " + MIN_PLAYERS + "~" + MAX_PLAYERS + "명이어야 합니다: " + playerCount);
                }
                if (custom.containsKey(playerCount)) {
                    throw new IllegalArgumentException(playerCount + "인 커스텀 구성이 두 번 들어 있습니다.");
                }
                List<String> codes = composition.roles() == null ? List.of() : composition.roles();
                List<RoleDefinition> resolved = new ArrayList<>();
                for (String code : codes) {
                    resolved.add(roleCatalog.find(code)
                            .orElseThrow(() -> new IllegalArgumentException("없는 직업입니다: " + code)));
                }
                String problem = problemOf(playerCount, resolved);
                if (problem != null) {
                    throw new IllegalArgumentException(playerCount + "인 커스텀 구성: " + problem);
                }
                custom.put(playerCount, List.copyOf(codes));
            }
        }

        List<String> candidates = specialRoleCodes;
        if (setup.randomCandidates() != null) {
            Set<String> picked = new HashSet<>();
            for (String code : setup.randomCandidates()) {
                if (roleCatalog.find(code).isEmpty()) {
                    throw new IllegalArgumentException("없는 직업입니다: " + code);
                }
                picked.add(code);
            }
            candidates = specialRoleCodes.stream().filter(picked::contains).toList();
        }

        List<RoleComposition> compositions = custom.entrySet().stream()
                .map(e -> new RoleComposition(e.getKey(), e.getValue()))
                .toList();
        return new RoleSetup(setup.mode(), compositions, candidates);
    }

    // ---------- 설정 화면용 정보 (GET /api/v1/role-setup/options) ----------

    /** 고를 수 있는 직업 전체. 해적 진영 먼저, 추천 구성에 먼저 등장하는 직업 먼저, 선원은 맨 뒤. */
    public List<RoleDefinition> roles() {
        return roles;
    }

    /** 랜덤 후보로 고를 수 있는 특수 직업. 해적과 선원은 항상 후보라 빠지고, 제3 세력은 후보가 될 수 없다. */
    public List<String> specialRoleCodes() {
        return specialRoleCodes;
    }

    /** 인원수별 추천 구성 (4~12인 순) */
    public List<RoleComposition> recommendedCompositions() {
        return IntStream.rangeClosed(MIN_PLAYERS, MAX_PLAYERS)
                .mapToObj(count -> new RoleComposition(count, RECOMMENDED.get(count)))
                .toList();
    }

    // ---------- 구성 만들기 ----------

    private List<RoleDefinition> customComposition(int playerCount, RoleSetup setup) {
        List<String> codes = setup.customFor(playerCount).orElse(null);
        if (codes == null) {
            return new ArrayList<>(recommended.get(playerCount));
        }
        List<RoleDefinition> resolved = new ArrayList<>();
        for (String code : codes) {
            resolved.add(roleCatalog.find(code).orElseThrow(() ->
                    new GameRuleException(playerCount + "인 커스텀 구성에 지금은 쓸 수 없는 직업이 있습니다: " + code)));
        }
        String problem = problemOf(playerCount, resolved);
        if (problem != null) {
            throw new GameRuleException(playerCount + "인 커스텀 구성: " + problem);
        }
        return resolved;
    }

    /**
     * 해적 진영 수는 추천 구성과 같다. 해적 1명은 항상 넣고(공격할 해적 보장),
     * 남은 해적 진영 자리는 해적(중복 가능)과 아직 안 뽑힌 해적 진영 후보 중에서 같은 확률로 고른다.
     * 선원 진영 자리는 선원 진영 후보를 섞어 앞에서부터 채우고, 후보가 모자라면 선원으로 채운다.
     * 특수 직업은 한 게임에 한 명씩만 나온다.
     */
    private List<RoleDefinition> randomComposition(int playerCount, List<String> candidateCodes) {
        List<RoleDefinition> pirateCandidates = new ArrayList<>();
        List<RoleDefinition> crewCandidates = new ArrayList<>();
        Set<String> picked = candidateCodes == null ? Set.copyOf(specialRoleCodes) : Set.copyOf(candidateCodes);
        for (String code : specialRoleCodes) {
            if (picked.contains(code)) {
                RoleDefinition role = roleCatalog.get(code);
                (role.isPirate() ? pirateCandidates : crewCandidates).add(role);
            }
        }

        int pirateSlots = (int) recommended.get(playerCount).stream().filter(RoleDefinition::isPirate).count();
        List<RoleDefinition> assigned = new ArrayList<>();
        assigned.add(raider);
        for (int i = 1; i < pirateSlots; i++) {
            int pick = random.nextInt(pirateCandidates.size() + 1);
            assigned.add(pick == pirateCandidates.size() ? raider : pirateCandidates.remove(pick));
        }

        Collections.shuffle(crewCandidates, random);
        for (int i = 0; i < playerCount - pirateSlots; i++) {
            assigned.add(i < crewCandidates.size() ? crewCandidates.get(i) : sailor);
        }
        return assigned;
    }

    // ---------- 검증 ----------

    /** 구성 규칙에 맞지 않으면 이유를, 맞으면 null을 돌려준다. */
    private static String problemOf(int playerCount, List<RoleDefinition> roles) {
        if (roles.size() != playerCount) {
            return "직업 수(" + roles.size() + ")가 인원수(" + playerCount + ")와 다릅니다.";
        }
        if (roles.stream().noneMatch(RoleDefinition::isRaider)) {
            return "공격할 수 있는 해적이 1명 이상 있어야 합니다.";
        }
        long pirates = roles.stream().filter(RoleDefinition::isPirate).count();
        if (pirates * 2 >= playerCount) {
            return "해적 진영(" + pirates + "명)은 선원 진영(" + (playerCount - pirates) + "명)보다 적어야 합니다.";
        }
        if (roles.stream().anyMatch(RoleDefinition::isMonkey) && roles.stream().noneMatch(RoleAssigner::isMonkeyDisguise)) {
            return "원숭이가 있으면 원숭이가 위장할 직업(원숭이를 뺀 선원 진영, 유령 선장, 인어)도 1명 이상 있어야 합니다.";
        }
        return null;
    }

    /** 추천 구성표의 코드를 RoleDefinition으로 바꾸면서 표가 규칙에 맞는지 검증한다. */
    private static Map<Integer, List<RoleDefinition>> resolveRecommended(RoleCatalog roleCatalog) {
        Map<Integer, List<RoleDefinition>> resolved = new LinkedHashMap<>();
        IntStream.rangeClosed(MIN_PLAYERS, MAX_PLAYERS).forEach(count -> {
            List<String> codes = RECOMMENDED.get(count);
            if (codes == null) {
                throw new IllegalStateException(count + "인 추천 구성이 없습니다.");
            }
            List<RoleDefinition> roles = codes.stream().map(roleCatalog::get).toList();
            String problem = problemOf(count, roles);
            if (problem != null) {
                throw new IllegalStateException(count + "인 추천 구성: " + problem);
            }
            resolved.put(count, roles);
        });
        return Map.copyOf(resolved);
    }

    /** 해적과 선원은 진영 자리를 채우는 기본 직업이라 중복될 수 있고 랜덤 후보 선택 대상이 아니다. */
    private static boolean isBaseRole(RoleDefinition role) {
        return MAFIA_ROLE.equals(role.code()) || CITIZEN_ROLE.equals(role.code());
    }

    /** 설정 화면 표시 순서: 해적 진영 먼저 → 해적 → 추천 구성에 처음 나오는 인원수 순 → 선원은 맨 뒤 → code */
    private static Comparator<RoleDefinition> displayOrder() {
        return Comparator.comparing((RoleDefinition role) -> !role.isPirate())
                .thenComparing(role -> !MAFIA_ROLE.equals(role.code()))
                .thenComparing(role -> CITIZEN_ROLE.equals(role.code()))
                .thenComparingInt(role -> firstRecommendedCount(role.code()))
                .thenComparing(RoleDefinition::code);
    }

    private static int firstRecommendedCount(String code) {
        return IntStream.rangeClosed(MIN_PLAYERS, MAX_PLAYERS)
                .filter(count -> RECOMMENDED.get(count).contains(code))
                .findFirst()
                .orElse(Integer.MAX_VALUE);
    }

    private static List<String> plus(List<String> base, String... more) {
        List<String> list = new ArrayList<>(base);
        list.addAll(List.of(more));
        return List.copyOf(list);
    }
}
