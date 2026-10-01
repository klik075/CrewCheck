package com.WhoisntCitizen_server.night.dto;

import com.WhoisntCitizen_server.game.entity.GamePhase;

import java.util.List;

/**
 * 접수 결과. 전원이 제출해 바로 다음 페이즈로 넘어갔으면 phase가 바뀌어 있다.
 * contactedPirateIds: 이번 제출로 앵무새가 접선했을 때 알게 된 해적 id. 그 외에는 빈 목록.
 */
public record NightActionResponse(boolean accepted, GamePhase phase, long phaseVersion, List<Long> contactedPirateIds) {
}