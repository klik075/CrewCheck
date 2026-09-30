package com.WhoisntCitizen_server.lobby.dto;

import com.WhoisntCitizen_server.lobby.domain.room.Room;
import lombok.AllArgsConstructor;
import lombok.Getter;

@Getter
@AllArgsConstructor
public class RoomResponseDto {
    private Long id;
    private String title;
    private Long hostUserId;
    private int maxPlayers;
    private int currentPlayers;

    public static RoomResponseDto from(Room room) {
        return new RoomResponseDto(
                room.getId(),
                room.getTitle(),
                room.getHostUserId(),
                room.getMaxPlayers(),
                room.getPlayers().size()
        );
    }
}