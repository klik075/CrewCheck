package com.WhoisntCitizen_server.game.entity;

import com.WhoisntCitizen_server.jobs.domain.ActionCode;
import com.WhoisntCitizen_server.jobs.domain.Faction;
import com.WhoisntCitizen_server.jobs.domain.RoleDefinition;
import lombok.AccessLevel;
import lombok.Getter;

import java.util.EnumMap;
import java.util.LinkedHashSet;
import java.util.List;
import java.util.Map;
import java.time.Instant;
import java.util.Set;

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

    // 능력별 사용 횟수. 밤 판정 시점에 기록한다(제출할 때는 검사만 한다). 차단당한 행동은 세지 않는다.
    @Getter(AccessLevel.NONE)
    private final Map<ActionCode, Integer> usedCounts = new EnumMap<>(ActionCode.class);
    // 마지막으로 자기 자신을 보호한 날. 이틀 연속 자기 보호 금지 판정용
    @Getter(AccessLevel.NONE)
    private Integer lastSelfProtectDay;
    // 요리사가 마지막으로 투표 금지를 적용한 날과 대상. 같은 대상 연속 금지 판정용 (차단당한 밤은 기록하지 않는다)
    @Getter(AccessLevel.NONE)
    private Integer lastVoteBanDay;
    @Getter(AccessLevel.NONE)
    private Long lastVoteBanTargetId;
    // 세이렌이 마지막으로 유혹에 성공한 날. 성공한 다음 밤은 쉰다 (실패·차단·넘기기는 기록하지 않는다)
    @Getter(AccessLevel.NONE)
    private Integer lastSeduceSuccessDay;
    // 크라켄이 표식을 남긴 플레이어. 발동하면 모두 처치하고 비운다.
    @Getter(AccessLevel.NONE)
    private final Set<Long> krakenMarks = new LinkedHashSet<>();
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

    private static Team initialTeam(RoleDefinition role) {
        if (role.isSiren()) {
            return Team.SIREN;
        }
        if (role.faction() == Faction.PIRATE) {
            return Team.PIRATE;
        }
        return role.faction() == Faction.NEUTRAL ? Team.NEUTRAL : Team.CREW;
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
