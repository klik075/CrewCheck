package com.WhoisntCitizen_server.game.dto;

import com.WhoisntCitizen_server.jobs.dto.RoleApiDtos.RoleView;

import java.util.List;

/**
 * 직업 배정 설정 화면에 필요한 정보 (GET /api/v1/role-setup/options). 서버가 켜져 있는 동안 바뀌지 않는다.
 *
 * @param roles            고를 수 있는 직업 전체 (화면 표시 순서: 해적 진영 → 선원 진영, 선원은 맨 뒤)
 * @param randomCandidates 랜덤 후보로 고를 수 있는 특수 직업. 해적과 선원은 항상 후보라 없다.
 * @param recommended      인원수별 추천 구성 (minPlayers~maxPlayers)
 */
public record RoleSetupOptionsResponse(
        int minPlayers,
        int maxPlayers,
        List<RoleView> roles,
        List<String> randomCandidates,
        List<RoleComposition> recommended
) {
}
