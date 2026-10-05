package com.WhoisntCitizen_server.game.dto;

import java.util.List;
import java.util.Optional;

/**
 * 방의 직업 배정 설정. 방(Redis)에 저장되고, 방장이 PUT /api/v1/rooms/{roomId}/role-setup 으로 통째로 바꾼다.
 * mode가 실제 배정 방식을 정하고, 나머지 두 값은 모드를 바꿔도 남아 있어 다시 고르면 그대로 쓸 수 있다.
 *
 * @param mode               배정 방식
 * @param customCompositions CUSTOM에서 쓰는 인원수별 구성. 없는 인원수는 추천 구성을 쓴다.
 * @param randomCandidates   RANDOM에서 뽑을 특수 직업 후보. 해적(PIRATE_RAIDER)과 선원(CREW_SAILOR)은 항상 후보라 넣지 않는다.
 *                           요청에서 null이면 모든 특수 직업으로 본다.
 */
public record RoleSetup(RoleSetupMode mode, List<RoleComposition> customCompositions, List<String> randomCandidates) {

    /** CUSTOM 표에서 해당 인원수의 구성. 방장이 편집하지 않은 인원수면 비어 있다. */
    public Optional<List<String>> customFor(int playerCount) {
        if (customCompositions == null) {
            return Optional.empty();
        }
        return customCompositions.stream()
                .filter(c -> c != null && c.playerCount() == playerCount)
                .map(RoleComposition::roles)
                .findFirst();
    }
}
