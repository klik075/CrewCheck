package com.WhoisntCitizen_server.lobby.dto;

import com.WhoisntCitizen_server.lobby.domain.room.Room;
import com.WhoisntCitizen_server.lobby.domain.room.RoomStatus;
import lombok.AllArgsConstructor;
import lombok.Getter;

import java.util.List;

/**
 * 방 단건 조회 응답 (GET /api/v1/rooms/{roomId}).
 * 대기 화면에서 주기적으로 조회해 참가자 목록을 갱신하고,
 * status가 IN_GAME으로 바뀌면 gameId로 게임 화면에 들어간다.
 */
@Getter
@AllArgsConstructor
public class RoomDetailResponseDto {
    private Long id;
    private String title;
    private Long hostUserId;
    private int maxPlayers;
    private int currentPlayers;
    private RoomStatus status;   // WAITING / IN_GAME
    private String gameId;       // 진행 중인 게임 id (대기 중이면 null)
    private List<RoomPlayerResponseDto> players;

    public static RoomDetailResponseDto from(Room room) {
        return new RoomDetailResponseDto(
                room.getId(),
                room.getTitle(),
                room.getHostUserId(),
                room.getMaxPlayers(),
                room.getPlayers().size(),
                room.getStatus(),
                room.getGameId(),
                room.getPlayers().stream().map(RoomPlayerResponseDto::from).toList()
        );
    }
}
