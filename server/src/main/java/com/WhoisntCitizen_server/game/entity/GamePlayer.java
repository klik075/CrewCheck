package com.WhoisntCitizen_server.game.entity;

import com.WhoisntCitizen_server.jobs.domain.ActionCode;
import com.WhoisntCitizen_server.jobs.domain.Faction;
import com.WhoisntCitizen_server.jobs.domain.RoleDefinition;
import lombok.AccessLevel;
import lombok.Getter;

import java.util.EnumMap;
import java.util.HashMap;
import java.util.LinkedHashSet;
import java.util.List;
import java.util.Map;
import java.time.Instant;
import java.util.Set;
import java.util.function.Supplier;

@Getter
public class GamePlayer {
    private final Long playerId;
    private final String nickname;
    // 실제 직업. 승리 판정, 게임 종료 후 공개, 다른 사람의 조사·주정뱅이 결과에 쓴다.
    private final RoleDefinition role;
    // 본인에게 보이는 직업이자 밤에 쓰는 능력. 원숭이만 role과 다르다(배정 시 정한 위장 직업).
    private final RoleDefinition shownRole;
    // 지금 속한 팀. 직업의 진영에서 시작하고, 세이렌에게 유혹당하면 SIREN이 된다(직업·능력은 그대로).
    private Team team;
    private boolean alive = true;
    // 사망한 시각. 살아 있으면 null. 사망자 채팅에서 "죽기 전에 다른 사망자들이 나눈 대화"를 거를 때 쓴다.
    private Instant diedAt;
    // 사망 원인. 살아 있거나 원인을 모르면 null. 인어의 처형 승리 판정에 쓴다.
    private DeathCause deathCause;

    // 아래 PACKAGE getter들은 저장(GameSnapshotMapper)용이다. 같은 패키지 밖(서비스 등)에서는 보이지 않는다.
    // 능력별 사용 횟수. 밤 판정 시점에 기록한다(제출할 때는 검사만 한다). 차단당한 행동은 세지 않는다.
    @Getter(AccessLevel.PACKAGE)
    private final Map<ActionCode, Integer> usedCounts = new EnumMap<>(ActionCode.class);
    // 마지막으로 자기 자신을 보호한 날. 이틀 연속 자기 보호 금지 판정용
    @Getter(AccessLevel.PACKAGE)
    private Integer lastSelfProtectDay;
    // 요리사가 마지막으로 투표 금지를 적용한 날과 대상. 같은 대상 연속 금지 판정용 (차단당한 밤은 기록하지 않는다)
    @Getter(AccessLevel.PACKAGE)
    private Integer lastVoteBanDay;
    @Getter(AccessLevel.PACKAGE)
    private Long lastVoteBanTargetId;
    // 세이렌이 마지막으로 유혹에 성공한 날. 성공한 다음 밤은 쉰다 (실패·차단·넘기기는 기록하지 않는다)
    @Getter(AccessLevel.PACKAGE)
    private Integer lastSeduceSuccessDay;
    // 크라켄이 표식을 남긴 플레이어. 발동하면 모두 처치하고 비운다.
    @Getter(AccessLevel.NONE)
    private final Set<Long> krakenMarks = new LinkedHashSet<>();
    // 원숭이가 받은 가짜 결과(대상 → 결과). 진짜는 같은 대상을 다시 봐도 결과가 같으므로 처음 정한 값을 게임 끝까지 쓴다.
    @Getter(AccessLevel.PACKAGE)
    private final Map<Long, Faction> fakeFactions = new HashMap<>();
    @Getter(AccessLevel.PACKAGE)
    private final Map<Long, RoleDefinition> fakeCorpseRoles = new HashMap<>();
    // 앵무새가 해적과 접선한 시각. null이면 접선 전. 해적 채팅 기록을 거를 때(7단계) 밤 도중 시각이 필요해 Instant로 둔다.
    private Instant contactedAt;
    // 세이렌에게 유혹당한 시각. null이면 유혹당하지 않음. 세이렌 팀 채팅을 이 시각부터 읽을 수 있다.
    private Instant seducedAt;
    // 연결이 끊겨 게임에서 내보낸 플레이어. 한 번 내보내면 다시 돌아와도 되돌리지 않는다.
    private boolean departed;

    /** 위장이 없는 플레이어. 보이는 직업이 실제 직업과 같다. */
    public GamePlayer(Long playerId, String nickname, RoleDefinition role) {
        this(playerId, nickname, role, role);
    }

    public GamePlayer(Long playerId, String nickname, RoleDefinition role, RoleDefinition shownRole) {
        this.playerId = playerId;
        this.nickname = nickname;
        this.role = role;
        this.shownRole = shownRole;
        this.team = initialTeam(role);
    }

    /**
     * 저장돼 있던 상태를 그대로 채운다. 저장소 복원(GameSnapshotMapper) 전용이며 규칙 검사를 하지 않는다.
     * (직업은 생성자로 넘기고, 생성자가 정한 처음 팀은 여기서 저장된 팀으로 덮어쓴다)
     */
    void restoreState(Team team, boolean alive, Instant diedAt, DeathCause deathCause,
                      Map<ActionCode, Integer> usedCounts, Integer lastSelfProtectDay,
                      Integer lastVoteBanDay, Long lastVoteBanTargetId, Integer lastSeduceSuccessDay,
                      List<Long> krakenMarks, Map<Long, Faction> fakeFactions,
                      Map<Long, RoleDefinition> fakeCorpseRoles,
                      Instant contactedAt, Instant seducedAt, boolean departed) {
        this.team = team;
        this.alive = alive;
        this.diedAt = diedAt;
        this.deathCause = deathCause;
        this.usedCounts.clear();
        this.usedCounts.putAll(usedCounts);
        this.lastSelfProtectDay = lastSelfProtectDay;
        this.lastVoteBanDay = lastVoteBanDay;
        this.lastVoteBanTargetId = lastVoteBanTargetId;
        this.lastSeduceSuccessDay = lastSeduceSuccessDay;
        this.krakenMarks.clear();
        this.krakenMarks.addAll(krakenMarks);
        this.fakeFactions.clear();
        this.fakeFactions.putAll(fakeFactions);
        this.fakeCorpseRoles.clear();
        this.fakeCorpseRoles.putAll(fakeCorpseRoles);
        this.contactedAt = contactedAt;
        this.seducedAt = seducedAt;
        this.departed = departed;
    }

    private static Team initialTeam(RoleDefinition role) {
        if (role.isSiren()) {
            return Team.SIREN;
        }
        if (role.faction() == Faction.PIRATE) {
            return Team.PIRATE;
        }
        return role.faction() == Faction.NEUTRAL ? Team.NEUTRAL : Team.CREW;
    }

    /**
     * 본인에게 보이는 팀. 원숭이가 제3 세력(유령 선장·인어)으로 위장했으면 아직 선원 팀인 동안 NEUTRAL로 보인다.
     * (진짜 유령 선장·인어는 NEUTRAL에서 시작하므로 CREW로 보이면 원숭이인 게 드러난다) 승리 판정은 실제 팀(team)을 쓴다.
     */
    public Team getShownTeam() {
        if (isMonkey() && team == Team.CREW) {
            return initialTeam(shownRole);
        }
        return team;
    }

    /** 원인을 모르는 사망 (테스트 등) */
    public void kill() {
        kill(null);
    }

    public void kill(DeathCause cause) {
        if (alive) {
            diedAt = Instant.now(); // 처음 사망한 시각만 기록
            deathCause = cause;
        }
        this.alive = false;
    }

    public void markDeparted() {
        this.departed = true;
    }

    public boolean isPirate() {
        return role.isPirate();
    }

    public boolean isRaider() {
        return role.isRaider();
    }

    public boolean isMonkey() {
        return role.isMonkey();
    }

    public boolean isParrot() {
        return role.isParrot();
    }

    public boolean isSiren() {
        return role.isSiren();
    }

    public boolean isKraken() {
        return role.isKraken();
    }

    public boolean isGhostCaptain() {
        return role.isGhostCaptain();
    }

    public boolean isMermaid() {
        return role.isMermaid();
    }

    /**
     * 해적 편으로 활동하는 플레이어: 해적 진영 중 접선하지 않은 앵무새만 뺀다. (해적, 요리사, 접선한 앵무새)
     * 해적 동료 목록, 해적 채팅, 승리 판정의 "해적 머릿수"가 모두 이 기준을 쓴다.
     */
    public boolean isActivePirate() {
        return isPirate() && (!isParrot() || isContacted());
    }

    // ---------- 앵무새 접선 ----------

    public boolean isContacted() {
        return contactedAt != null;
    }

    /** 한 번 접선하면 되돌리지 않는다. 이미 접선했으면 처음 시각을 유지한다. */
    public void markContacted(Instant at) {
        if (contactedAt == null) {
            contactedAt = at;
        }
    }

    // ---------- 세이렌 유혹 ----------

    /** 세이렌이 유혹할 수 있는 사람: 아직 선원 팀인 선원 진영, 또는 아직 유혹되지 않은 인어 */
    public boolean isSeducible() {
        return team == Team.CREW || (isMermaid() && team == Team.NEUTRAL);
    }

    /** 세이렌 팀으로 바꾼다. 직업과 능력은 그대로다. */
    public void joinSirenTeam(Instant at) {
        this.team = Team.SIREN;
        this.seducedAt = at;
    }

    public void recordSeduceSuccess(int day) {
        this.lastSeduceSuccessDay = day;
    }

    /** day 밤에 유혹에 성공했는지 (성공한 다음 밤은 쉰다) */
    public boolean seducedOn(int day) {
        return lastSeduceSuccessDay != null && lastSeduceSuccessDay == day;
    }

    // ---------- 크라켄 표식 ----------

    public void addKrakenMark(Long targetId) {
        krakenMarks.add(targetId);
    }

    public boolean hasKrakenMark(Long targetId) {
        return krakenMarks.contains(targetId);
    }

    /** 표식을 남긴 플레이어 (남긴 순서) */
    public List<Long> getKrakenMarks() {
        return List.copyOf(krakenMarks);
    }

    public void clearKrakenMarks() {
        krakenMarks.clear();
    }

    // ---------- 원숭이 가짜 결과 ----------

    /** 원숭이 선장이 targetId를 조사해 받는 가짜 진영. 처음 조사할 때만 draw로 정하고, 다시 조사하면 같은 값을 준다. */
    public Faction fakeFactionOf(Long targetId, Supplier<Faction> draw) {
        return fakeFactions.computeIfAbsent(targetId, id -> draw.get());
    }

    /** 원숭이 주정뱅이가 targetId의 시체에서 읽는 가짜 직업. 처음 읽을 때만 draw로 정하고, 다시 읽으면 같은 값을 준다. */
    public RoleDefinition fakeCorpseRoleOf(Long targetId, Supplier<RoleDefinition> draw) {
        return fakeCorpseRoles.computeIfAbsent(targetId, id -> draw.get());
    }

    // ---------- 능력 사용 기록 ----------

    /** 남은 사용 횟수. 무제한(maxUses = -1)이면 -1. */
    public int remainingUses(ActionCode code) {
        if (code.maxUses() < 0) {
            return -1;
        }
        return Math.max(0, code.maxUses() - usedCounts.getOrDefault(code, 0));
    }

    public boolean hasUsesLeft(ActionCode code) {
        return code.maxUses() < 0 || remainingUses(code) > 0;
    }

    public void recordUse(ActionCode code) {
        if (code.maxUses() >= 0) {
            usedCounts.merge(code, 1, Integer::sum);
        }
    }

    public void recordSelfProtect(int day) {
        this.lastSelfProtectDay = day;
    }

    public boolean selfProtectedOn(int day) {
        return lastSelfProtectDay != null && lastSelfProtectDay == day;
    }

    public void recordVoteBan(int day, Long targetId) {
        this.lastVoteBanDay = day;
        this.lastVoteBanTargetId = targetId;
    }

    /** day 밤에 targetId에게 투표 금지를 적용했는지 */
    public boolean voteBannedOn(int day, Long targetId) {
        return lastVoteBanDay != null && lastVoteBanDay == day && targetId.equals(lastVoteBanTargetId);
    }
}
