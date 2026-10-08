package com.WhoisntCitizen_server.game.repository.snapshot;

import com.WhoisntCitizen_server.game.entity.DeathCause;
import com.WhoisntCitizen_server.game.entity.Team;
import com.WhoisntCitizen_server.jobs.domain.ActionCode;
import com.WhoisntCitizen_server.jobs.domain.Faction;

import java.time.Instant;
import java.util.List;
import java.util.Map;

/**
 * GamePlayer 한 명의 저장 형식. GamePlayer의 모든 필드를 빠짐없이 담는다.
 * (getter가 없는 내부 상태 — 능력 사용 횟수, 연속 사용 제한 기록, 크라켄 표식, 원숭이 가짜 결과 — 포함)
 *
 * GamePlayer에 필드를 추가하면 여기에도 추가하고 GameSnapshot.CURRENT_SCHEMA_VERSION 변경을 검토한다.
 */
public record PlayerSnapshot(
        Long playerId,
        String nickname,
        RoleSnapshot role,                       // 실제 직업
        RoleSnapshot shownRole,                  // 보이는 직업 (원숭이만 role과 다름)
        Team team,
        boolean alive,
        Instant diedAt,
        DeathCause deathCause,
        Map<ActionCode, Integer> usedCounts,     // 능력별 사용 횟수
        Integer lastSelfProtectDay,
        Integer lastVoteBanDay,
        Long lastVoteBanTargetId,
        Integer lastSeduceSuccessDay,
        List<Long> krakenMarks,                  // 표식 남긴 순서 유지
        Map<Long, Faction> fakeFactions,         // 원숭이 가짜 조사 결과 (대상 → 진영)
        Map<Long, RoleSnapshot> fakeCorpseRoles, // 원숭이 가짜 시체 확인 결과 (대상 → 직업)
        Instant contactedAt,
        Instant seducedAt,
        boolean departed
) {
    public PlayerSnapshot {
        usedCounts = usedCounts == null ? Map.of() : Map.copyOf(usedCounts);
        krakenMarks = krakenMarks == null ? List.of() : List.copyOf(krakenMarks);
        fakeFactions = fakeFactions == null ? Map.of() : Map.copyOf(fakeFactions);
        fakeCorpseRoles = fakeCorpseRoles == null ? Map.of() : Map.copyOf(fakeCorpseRoles);
    }
}
