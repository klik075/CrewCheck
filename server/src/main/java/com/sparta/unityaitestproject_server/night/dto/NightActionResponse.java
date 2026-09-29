package com.sparta.unityaitestproject_server.night.dto;

import com.sparta.unityaitestproject_server.game.entity.GamePhase;

/** 접수 결과. 전원이 제출해 바로 다음 페이즈로 넘어갔으면 phase가 바뀌어 있다. */
public record NightActionResponse(boolean accepted, GamePhase phase, long phaseVersion) {
}
