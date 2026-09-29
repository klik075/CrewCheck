package com.sparta.unityaitestproject_server.game.dto;

import jakarta.validation.Valid;
import jakarta.validation.constraints.NotBlank;
import jakarta.validation.constraints.NotNull;
import jakarta.validation.constraints.Size;

import java.util.List;

/** 1. 게임 시작 요청. 방(room)에 모인 플레이어 목록을 그대로 전달한다. */
public record StartGameRequest(
        @NotBlank String roomId,
        @NotNull @Size(min = 4, max = 12) List<@Valid PlayerEntry> players
){
    public record PlayerEntry(
            @NotNull Long playerId, @NotBlank String nickname
    ){

    }
}
