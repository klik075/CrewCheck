package com.WhoisntCitizen_server.lobby.controller;

import com.WhoisntCitizen_server.game.dto.StartGameResponse;
import com.WhoisntCitizen_server.lobby.dto.CreateRoomRequestDto;
import com.WhoisntCitizen_server.lobby.dto.JoinRoomRequestDto;
import com.WhoisntCitizen_server.lobby.dto.RoomDetailResponseDto;
import com.WhoisntCitizen_server.lobby.dto.RoomPlayerResponseDto;
import com.WhoisntCitizen_server.lobby.dto.RoomResponseDto;
import com.WhoisntCitizen_server.lobby.dto.SetReadyRequestDto;
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

    /**
     * 방 단건 조회: 방 정보 + 상태(WAITING/IN_GAME) + gameId + 참가자 목록.
     * 대기 화면에서 주기적으로 호출해, status가 IN_GAME이 되면 gameId로 게임 화면에 들어간다.
     */
    @GetMapping("/{roomId}")
    public ResponseEntity<RoomDetailResponseDto> getRoom(@PathVariable Long roomId) {
        return ResponseEntity.ok(roomService.getRoom(roomId));
    }

    /**
     * 방 입장. 공개방/비밀방 모두 이 API 하나를 쓴다.
     * body는 선택: 공개방은 body 없이, 비밀방은 {"password":"0427"}을 보낸다.
     * required = false라서 body가 없으면 request가 null로 들어온다. (기존 Unity 클라이언트와 호환)
     * 실패: 403 WRONG_ROOM_PASSWORD 비밀번호 없음/불일치, 409 게임 중 / 정원 초과 / 이미 참가 중, 400 존재하지 않는 방
     */
    @PostMapping("/{roomId}/players")
    public ResponseEntity<RoomResponseDto> joinRoom(
            @PathVariable Long roomId,
            @AuthenticationPrincipal Jwt jwt,
            @RequestBody(required = false) JoinRoomRequestDto request
    ) {
        String password = request != null ? request.getPassword() : null;
        return ResponseEntity.ok(roomService.joinRoom(roomId, memberId(jwt), password));
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
     * 준비 / 준비 취소 (방장 제외). body: {"ready": true} 또는 {"ready": false}
     * 상태를 "정하는" 요청이라 같은 요청을 여러 번 보내도 결과가 같다. 성공 시 204.
     * 실패: 400 body 없음 / ready 누락 / 존재하지 않는 방, 409 게임 중 / 참가 중이 아님 / 방장
     */
    @PutMapping("/{roomId}/players/me/ready")
    public ResponseEntity<Void> setReady(
            @PathVariable Long roomId,
            @AuthenticationPrincipal Jwt jwt,
            @RequestBody(required = false) SetReadyRequestDto request
    ) {
        if (request == null || request.getReady() == null) throw new IllegalArgumentException("ready 값(true/false)을 보내주세요.");

        roomService.setReady(roomId, memberId(jwt), request.getReady());
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

    /**
     * 방 목록. keyword를 주면 제목으로 검색한다. (선택, 없으면 전체 목록)
     *   GET /api/v1/rooms               전체
     *   GET /api/v1/rooms?keyword=초보   제목에 "초보"가 들어간 방 (부분 일치, 대소문자·공백 무시)
     * 실패: 400 검색어가 30자 초과
     */
    @GetMapping
    public ResponseEntity<List<RoomResponseDto>> getRooms(
            @RequestParam(required = false) String keyword
    ) {
        return ResponseEntity.ok(roomService.getRooms(keyword));
    }

    // 토큰 sub = memberId. User(프로필) 조회는 service에서 한다.
    private static Long memberId(Jwt jwt) {
        return Long.valueOf(jwt.getSubject());
    }
}
