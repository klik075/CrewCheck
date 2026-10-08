package com.WhoisntCitizen_server.game.repository.snapshot;

import com.WhoisntCitizen_server.game.entity.GameEndReason;
import com.WhoisntCitizen_server.game.entity.GamePhase;
import com.WhoisntCitizen_server.game.entity.Winner;
import com.WhoisntCitizen_server.night.entity.NightAction;
import com.WhoisntCitizen_server.night.entity.NightResult;
import com.WhoisntCitizen_server.vote.entity.ExecutionResult;

import java.time.Instant;
import java.util.ArrayList;
import java.util.List;

/**
 * 게임 한 판의 저장 형식(스냅샷). Redis에는 이 record를 JSON으로 바꿔 저장한다.
 *
 * Game을 그대로 JSON으로 만들지 않는 이유
 *  - Game의 생성자는 새 게임용이라 gameId를 새로 만들고 createdAt을 지금 시각으로 넣는다. 저장된 게임을 되살릴 수 없다.
 *  - getter가 없는 내부 상태(lockedActors, skippedActors, confirmedVoters, voteBanned 등)가 있어 그대로는 저장되지 않는다.
 *  - 저장 형식이 도메인 코드에 묶이면, 필드 이름만 바꿔도 이미 저장된 게임을 읽지 못하게 된다.
 *  그래서 "무엇을 저장하는가"를 이 record로 따로 정하고, Game ↔ GameSnapshot 변환(1-2)으로 잇는다.
 *
 * 형식 규칙
 *  - Game의 모든 필드를 빠짐없이 담는다. (1-3 왕복 테스트가 빠진 필드를 잡는다)
 *  - 순서가 의미 있는 값(플레이어 입장 순서, 밤 행동·투표 제출 순서, 크라켄 표식 순서)은 List로 순서를 그대로 둔다.
 *  - 순서가 의미 없는 집합(Set)은 정렬한 List로 둔다. 같은 상태면 항상 같은 JSON이 나와 비교·디버깅이 쉽다.
 *  - null 목록은 빈 목록으로 바꾼다.
 *  - NightAction, NightResult, ExecutionResult는 판단 메서드가 없는 불변 record라 그대로 담는다.
 *    이 record들의 필드 이름을 바꾸면 저장 형식도 바뀌므로 schemaVersion을 올리고 옛 형식을 읽는 방법을 정한다.
 *
 * Game에 필드를 추가하면 여기에도 추가하고, 옛 데이터와 호환되지 않으면 CURRENT_SCHEMA_VERSION을 올린다.
 */
public record GameSnapshot(
        int schemaVersion,

        // 게임 기본 정보 (변하지 않음)
        String gameId,
        String roomId,
        boolean recordStats,
        Instant createdAt,

        // 페이즈
        GamePhase phase,
        int day,
        long phaseVersion,
        Instant phaseEndsAt,

        // 플레이어 (입장 순서)
        List<PlayerSnapshot> players,

        // 밤 (이번 밤 기준, 다음 밤에 들어가면 비워짐)
        List<NightAction> nightActions,  // 제출 순서. actorId는 NightAction 안에 있음
        List<Long> lockedActors,         // 행동이 확정된 플레이어 (접선한 앵무새)
        List<Long> skippedActors,        // 능력 사용을 넘긴 플레이어

        // 투표 (이번 투표 기준)
        List<VoteEntry> votes,           // 투표 순서
        List<Long> confirmedVoters,      // "투표 완료"를 누른 플레이어
        List<Long> voteBanned,           // 요리사 때문에 오늘 투표를 못 하는 플레이어

        // 사망자 없는 날 세기
        int lastDeathDay,

        // 결과
        NightResult lastNightResult,
        ExecutionResult lastExecutionResult,
        Winner winner,
        List<Long> winnerIds,
        GameEndReason endReason
) {
    /** 지금 코드가 쓰는 저장 형식 버전. 호환되지 않게 형식을 바꾸면 올린다. */
    public static final int CURRENT_SCHEMA_VERSION = 1;

    /** 투표 한 건 (voterId → targetId). Map 대신 목록으로 두어 투표 순서를 그대로 남긴다. */
    public record VoteEntry(Long voterId, Long targetId) {
    }

    public GameSnapshot {
        players = copy(players);
        nightActions = copy(nightActions);
        lockedActors = sortedCopy(lockedActors);
        skippedActors = sortedCopy(skippedActors);
        votes = copy(votes);
        confirmedVoters = sortedCopy(confirmedVoters);
        voteBanned = sortedCopy(voteBanned);
        winnerIds = copy(winnerIds);
    }

    private static <T> List<T> copy(List<T> list) {
        return list == null ? List.of() : List.copyOf(list);
    }

    private static List<Long> sortedCopy(List<Long> list) {
        if (list == null) {
            return List.of();
        }
        List<Long> sorted = new ArrayList<>(list);
        sorted.sort(null);
        return List.copyOf(sorted);
    }
}
