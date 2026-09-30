package com.WhoisntCitizen_server.lobby.controller;

import com.WhoisntCitizen_server.lobby.domain.room.Room;
import com.WhoisntCitizen_server.lobby.domain.room.RoomPlayer;
import com.WhoisntCitizen_server.lobby.dto.CreateRoomRequestDto;
import com.WhoisntCitizen_server.lobby.dto.JoinRoomRequestDto;
import com.WhoisntCitizen_server.lobby.dto.RoomPlayerResponseDto;
import com.WhoisntCitizen_server.lobby.dto.RoomResponseDto;
import com.WhoisntCitizen_server.lobby.service.RoomService;
import lombok.RequiredArgsConstructor;
import org.springframework.http.HttpStatus;
import org.springframework.http.ResponseEntity;
import org.springframework.web.bind.annotation.*;

import java.util.List;

@RestController
@RequiredArgsConstructor
@RequestMapping("/api/v1/rooms")
public class RoomController {

    private final RoomService roomService;

    @PostMapping
    public ResponseEntity<RoomResponseDto> createRoom(
            @RequestBody CreateRoomRequestDto request
    ) {
        return ResponseEntity
                .status(HttpStatus.CREATED)
                .body(roomService.createRoom(request));
    }

    @PostMapping("/{roomId}/players")
    public ResponseEntity<RoomResponseDto> joinRoom(
            @PathVariable Long roomId,
            @RequestBody JoinRoomRequestDto request
    ) {
        return ResponseEntity.ok(roomService.joinRoom(roomId, request));
    }

    @GetMapping("/{roomId}/players")
    public ResponseEntity<List<RoomPlayerResponseDto>> getPlayers(
            @PathVariable Long roomId
    ) {
        return ResponseEntity.ok(roomService.getPlayers(roomId));
    }

    @DeleteMapping("/{roomId}/players/{userId}")
    public ResponseEntity<Void> leaveRoom(
            @PathVariable Long roomId,
            @PathVariable Long userId
    ) {
        roomService.leaveRoom(roomId, userId);
        return ResponseEntity.noContent().build();
    }

    @GetMapping
    public ResponseEntity<List<RoomResponseDto>> getRooms() {
        return ResponseEntity.ok(roomService.getRooms());
    }
}
