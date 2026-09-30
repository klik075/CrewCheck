package com.WhoisntCitizen_server.lobby.dto;

import lombok.Getter;

@Getter
public class CreateRoomRequestDto {
    private Long userId;
    private String title;
    private int maxPlayers;
}