package com.WhoisntCitizen_server.game.entity;

import com.WhoisntCitizen_server.jobs.domain.ActionCode;
import com.WhoisntCitizen_server.jobs.domain.RoleDefinition;
import lombok.AccessLevel;
import lombok.Getter;

import java.util.EnumMap;
import java.util.Map;
import java.time.Instant;

@Getter
public class GamePlayer {
    private final Long playerId;
    private final String nickname;
    // 실제 직업. 승리 판정, 게임 종료 후 공개, 다른 사람의 조사·주정뱅이 결과에 쓴다.
    private final RoleDefinition role;
    // 본인에게 보이는 직업이자 밤에 쓰는 능력. 원숭이만 role과 다르다(배정 시 정한 위장 직업).
    private final RoleDefinition shownRole;
    private boolean alive = true;
    // 사망한 시각. 살아 있으면 null. 사망자 채팅에서 "죽기 전에 다른 사망자들이 나눈 대화"를 거를 때 쓴다.
    private Instant diedAt;

    // 능력별 사용 횟수. 밤 판정 시점에 기록한다(제출할 때는 검사만 한다). 차단당한 행동은 세지 않는다.
    @Getter(AccessLevel.NONE)
    private final Map<ActionCode, Integer> usedCounts = new EnumMap<>(ActionCode.class);
    // 마지막으로 자기 자신을 보호한 날. 이틀 연속 자기 보호 금지 판정용
    @Getter(AccessLevel.NONE)
    private Integer lastSelfProtectDay;
    // 앵무새가 해적과 접선한 시각. null이면 접선 전. 해적 채팅 기록을 거를 때(7단계) 밤 도중 시각이 필요해 Instant로 둔다.
    private Instant contactedAt;

    /** 위장이 없는 플레이어. 보이는 직업이 실제 직업과 같다. */
    public GamePlayer(Long playerId, String nickname, RoleDefinition role) {
        this(playerId, nickname, role, role);
    }

    public GamePlayer(Long playerId, String nickname, RoleDefinition role, RoleDefinition shownRole) {
        this.playerId = playerId;
        this.nickname = nickname;
        this.role = role;
        this.shownRole = shownRole;
    }

    public void kill() {
        if (alive) {
            diedAt = Instant.now(); // 처음 사망한 시각만 기록
        }
        this.alive = false;
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
}