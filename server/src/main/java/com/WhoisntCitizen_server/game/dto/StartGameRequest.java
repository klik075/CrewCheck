package com.WhoisntCitizen_server.game.dto;

import jakarta.validation.Valid;
import jakarta.validation.constraints.NotBlank;
import jakarta.validation.constraints.NotNull;
import jakarta.validation.constraints.Size;

import java.util.List;

/**
 * 1. 게임 시작 요청. 방(room)에 모인 플레이어 목록을 그대로 전달한다.
 * roleSetup은 선택이다. 없으면 추천 구성으로 배정하고, 주면 방의 직업 배정 설정과 같은 형식·규칙을 따른다.
 */
public record StartGameRequest(
        @NotBlank String roomId,
        @NotNull @Size(min = 4, max = 12) List<@Valid PlayerEntry> players,
        RoleSetup roleSetup
){
    public record PlayerEntry(
            @NotNull Long playerId, @NotBlank String nickname
    ){

    }
}
