package com.WhoisntCitizen_server.lobby.dto;


import com.WhoisntCitizen_server.lobby.domain.room.RoomPlayer;
import lombok.AllArgsConstructor;
import lombok.Getter;

@Getter
@AllArgsConstructor
public class RoomPlayerResponseDto {

    private Long userId;
    private String nickname;
    private boolean ready;

    public static RoomPlayerResponseDto from(RoomPlayer roomPlayer) {
        return new RoomPlayerResponseDto(
                roomPlayer.getUserId(),
                roomPlayer.getNickname(),
                roomPlayer.isReady()
        );
    }
}