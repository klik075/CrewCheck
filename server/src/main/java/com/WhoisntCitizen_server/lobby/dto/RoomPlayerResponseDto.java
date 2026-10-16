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
    private int profileImageId; // 대기실 초상화 (아바타 프리셋 번호)

    public static RoomPlayerResponseDto from(RoomPlayer roomPlayer) {
        return new RoomPlayerResponseDto(
                roomPlayer.getUserId(),
                roomPlayer.getNickname(),
                roomPlayer.isReady(),
                roomPlayer.getProfileImageId()
        );
    }
}