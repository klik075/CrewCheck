package com.WhoisntCitizen_server.lobby.dto;

import lombok.Getter;
import lombok.NoArgsConstructor;

// 방장 id는 토큰(JWT)에서 꺼내므로 요청 body에는 받지 않는다.
@Getter
@NoArgsConstructor
public class CreateRoomRequestDto {
    private String title;
    private int maxPlayers;
}
