package com.WhoisntCitizen_server.lobby.dto;

import com.WhoisntCitizen_server.lobby.domain.room.Room;
import com.WhoisntCitizen_server.lobby.domain.room.RoomStatus;
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
    private RoomStatus status; // WAITING / IN_GAME (Unity 로비에서 "게임 중" 표시, 입장 버튼 비활성화)
    private String gameId;     // 진행 중인 게임 id (대기 중이면 null). 방장이 아닌 참가자는 방을 조회해 이 값으로 게임 화면에 들어간다

    public static RoomResponseDto from(Room room) {
        return new RoomResponseDto(
                room.getId(),
                room.getTitle(),
                room.getHostUserId(),
                room.getMaxPlayers(),
                room.getPlayers().size(),
                room.getStatus(),
                room.getGameId()
        );
    }
}