package com.WhoisntCitizen_server.lobby.controller;

import com.WhoisntCitizen_server.game.dto.StartGameResponse;
import com.WhoisntCitizen_server.lobby.dto.CreateRoomRequestDto;
import com.WhoisntCitizen_server.lobby.dto.RoomPlayerResponseDto;
import com.WhoisntCitizen_server.lobby.dto.RoomResponseDto;
import com.WhoisntCitizen_server.lobby.service.RoomService;
import lombok.RequiredArgsConstructor;
import org.springframework.http.HttpStatus;
import org.springframework.http.ResponseEntity;
import org.springframework.security.core.annotation.AuthenticationPrincipal;
import org.springframework.security.oauth2.jwt.Jwt;
import org.springframework.web.bind.annotation.*;

import java.util.List;

/**
 * 모든 요청은 헤더에 Authorization: Bearer {로그인 때 받은 accessToken} 이 필요하다.
 * 요청 body가 아니라 토큰의 sub(memberId)로 로그인 유저를 식별하고,
 * 룸 안에서는 그 memberId로 찾은 User(프로필)의 id를 userId로 사용한다.
 */
@RestController
@RequiredArgsConstructor
@RequestMapping("/api/v1/rooms")
public class RoomController {

    private final RoomService roomService;

    @PostMapping
    public ResponseEntity<RoomResponseDto> createRoom(
            @AuthenticationPrincipal Jwt jwt,
            @RequestBody CreateRoomRequestDto request
    ) {
        return ResponseEntity
                .status(HttpStatus.CREATED)
                .body(roomService.createRoom(memberId(jwt), request));
    }

    @PostMapping("/{roomId}/players")
    public ResponseEntity<RoomResponseDto> joinRoom(
            @PathVariable Long roomId,
            @AuthenticationPrincipal Jwt jwt
    ) {
        return ResponseEntity.ok(roomService.joinRoom(roomId, memberId(jwt)));
    }

    @GetMapping("/{roomId}/players")
    public ResponseEntity<List<RoomPlayerResponseDto>> getPlayers(
            @PathVariable Long roomId
    ) {
        return ResponseEntity.ok(roomService.getPlayers(roomId));
    }

    // 본인만 나갈 수 있도록 path의 userId 대신 토큰 사용
    @DeleteMapping("/{roomId}/players/me")
    public ResponseEntity<Void> leaveRoom(
            @PathVariable Long roomId,
            @AuthenticationPrincipal Jwt jwt
    ) {
        roomService.leaveRoom(roomId, memberId(jwt));
        return ResponseEntity.noContent().build();
    }

    /**
     * 게임 시작 (방장만): 이 방에 게임을 생성한다. 성공하면 방이 IN_GAME이 되고 gameId를 돌려준다.
     * 다른 참가자는 방 조회(GET /api/v1/rooms)의 gameId로 게임 API(/api/v1/games/{gameId})에 접근한다.
     */
    @PostMapping("/{roomId}/games")
    public ResponseEntity<StartGameResponse> startGame(
            @PathVariable Long roomId,
            @AuthenticationPrincipal Jwt jwt
    ) {
        return ResponseEntity
                .status(HttpStatus.CREATED)
                .body(roomService.startGame(roomId, memberId(jwt)));
    }

    @GetMapping
    public ResponseEntity<List<RoomResponseDto>> getRooms() {
        return ResponseEntity.ok(roomService.getRooms());
    }

    // 토큰 sub = memberId. User(프로필) 조회는 service에서 한다.
    private static Long memberId(Jwt jwt) {
        return Long.valueOf(jwt.getSubject());
    }
}
