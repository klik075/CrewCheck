package com.WhoisntCitizen_server.lobby.controller;

import com.WhoisntCitizen_server.game.dto.RoleSetup;
import com.WhoisntCitizen_server.game.dto.StartGameResponse;
import com.WhoisntCitizen_server.lobby.dto.CreateRoomRequestDto;
import com.WhoisntCitizen_server.lobby.dto.JoinRoomRequestDto;
import com.WhoisntCitizen_server.lobby.dto.RoomDetailResponseDto;
import com.WhoisntCitizen_server.lobby.dto.RoomPlayerResponseDto;
import com.WhoisntCitizen_server.lobby.dto.RoomResponseDto;
import com.WhoisntCitizen_server.lobby.dto.SetReadyRequestDto;
import com.WhoisntCitizen_server.lobby.dto.TransferHostRequestDto;
import com.WhoisntCitizen_server.lobby.dto.UpdateRoomSettingsRequestDto;
import com.WhoisntCitizen_server.lobby.presence.RoomPresenceService;
import com.WhoisntCitizen_server.lobby.service.RoomService;
import com.WhoisntCitizen_server.user.service.CurrentUserResolver;
import lombok.RequiredArgsConstructor;
import org.springframework.http.HttpStatus;
import org.springframework.http.ResponseEntity;
import org.springframework.security.core.annotation.AuthenticationPrincipal;
import org.springframework.security.oauth2.jwt.Jwt;
import org.springframework.web.bind.annotation.*;

import java.util.List;

/**
 * 로비(방) API.
 * 모든 요청은 헤더에 Authorization: Bearer {로그인 때 받은 accessToken} 이 필요하다.
 *
 * "요청한 사람" 식별
 *   - 요청 body가 아니라 토큰으로 식별한다. (다른 사람인 척 요청할 수 없도록)
 *   - 토큰의 sub는 memberId(로그인 계정 id)이고, 방에서는 userId(User.id, 프로필 id)를 쓴다.
 *   - 변환은 CurrentUserResolver.userId(jwt)가 맡고, 서비스(RoomService)에는 userId만 넘긴다.
 *     게임·투표·밤 컨트롤러(GameController, VoteController, NightController)와 같은 방식이다.
 *   - 이렇게 "토큰 → 나" 변환을 한 곳에 모아 두면, 변환 방식이 바뀌어도(캐시, 토큰에 userId 넣기 등)
 *     Resolver만 고치면 되고, 로비 서비스는 계정(Member)을 전혀 몰라도 된다.
 *
 * 방 목록·참가자 목록 조회는 "누가" 요청했는지 쓰지 않으므로 jwt 파라미터를 받지 않는다.
 * 방 단건 조회는 대기실 접속 기록을 남기기 위해 jwt로 요청한 사람을 구한다.
 * (토큰 자체는 SecurityConfig에서 똑같이 검사한다)
 */
@RestController
@RequiredArgsConstructor
@RequestMapping("/api/v1/rooms")
public class RoomController {

    private final RoomService roomService;
    // 토큰(sub = memberId) → userId 변환. 이 컨트롤러에서 "요청한 사람"은 항상 이걸로 구한다.
    private final CurrentUserResolver currentUser;
    // 대기실 접속 기록 (방 조회가 idleTimeout(기본 45초) 넘게 끊기면 방에서 뺀다)
    private final RoomPresenceService roomPresenceService;

    @PostMapping
    public ResponseEntity<RoomResponseDto> createRoom(
            @AuthenticationPrincipal Jwt jwt,
            @RequestBody CreateRoomRequestDto request
    ) {
        return ResponseEntity
                .status(HttpStatus.CREATED)
                .body(roomService.createRoom(currentUser.userId(jwt), request));
    }

    /**
     * 방 단건 조회: 방 정보 + 상태(WAITING/IN_GAME) + gameId + 참가자 목록.
     * 대기 화면에서 주기적으로 호출해, status가 IN_GAME이 되면 gameId로 게임 화면에 들어간다.
     *
     * 대기실은 이 API를 1초마다(웹소켓으로 방을 구독 중이면 15초마다) 부르므로 "아직 접속 중"이라는 신호(대기실 접속 기록)로도 쓴다.
     *  - 기록이 idleTimeout(기본 45초) 넘게 끊기면 RoomPresenceService가 이 사람을 방에서 뺀다.
     *  - 조회보다 먼저 기록한다: 조회를 처리하는 사이 검사가 돌아도 방금 요청한 사람이 빠지지 않도록.
     *  - 참가자인지는 여기서 확인하지 않는다. (참가자가 아닌 사람의 기록은 다음 검사에서 지워진다)
     */
    @GetMapping("/{roomId}")
    public ResponseEntity<RoomDetailResponseDto> getRoom(
            @PathVariable Long roomId,
            @AuthenticationPrincipal Jwt jwt
    ) {
        roomPresenceService.touch(roomId, currentUser.userId(jwt));
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
        return ResponseEntity.ok(roomService.joinRoom(roomId, currentUser.userId(jwt), password));
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
        roomService.leaveRoom(roomId, currentUser.userId(jwt));
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

        roomService.setReady(roomId, currentUser.userId(jwt), request.getReady());
        return ResponseEntity.noContent().build();
    }

    /**
     * 방장 위임 (방장만). body: {"userId": 5}  성공 시 204.
     * 실패: 403 방장 아님, 409 게임 중 / 대상이 방에 없음, 400 body·userId 없음 / 자기 자신 / 존재하지 않는 방
     */
    @PutMapping("/{roomId}/host")
    public ResponseEntity<Void> transferHost(
            @PathVariable Long roomId,
            @AuthenticationPrincipal Jwt jwt,
            @RequestBody(required = false) TransferHostRequestDto request
    ) {
        if (request == null || request.getUserId() == null) throw new IllegalArgumentException("위임할 플레이어(userId)를 보내주세요.");

        roomService.transferHost(roomId, currentUser.userId(jwt), request.getUserId());
        return ResponseEntity.noContent().build();
    }

    /**
     * 방 설정 변경 (방장만). body: {"title":"초보만","maxPlayers":10,"privateRoom":true,"password":"0427"}  성공 시 204.
     * 설정 전체를 보낸다. 비밀방을 비밀방으로 유지하면서 password를 비워 보내면("" 또는 null) 기존 비밀번호를 유지한다.
     * 참가자의 준비 상태는 그대로 둔다. 바뀐 것이 있으면 채팅에 "방 설정이 변경되었습니다" 시스템 메시지가 남는다.
     * 실패: 403 방장 아님, 409 게임 중 / 최대 인원 < 현재 인원,
     *       400 body 없음 / 제목 없음 / 인원 범위(4~12) 밖 / maxPlayers·privateRoom 누락 / 비밀번호 형식(숫자 4자리 이상) / 존재하지 않는 방
     */
    @PutMapping("/{roomId}/settings")
    public ResponseEntity<Void> updateSettings(
            @PathVariable Long roomId,
            @AuthenticationPrincipal Jwt jwt,
            @RequestBody(required = false) UpdateRoomSettingsRequestDto request
    ) {
        if (request == null) throw new IllegalArgumentException("변경할 방 설정을 보내주세요.");

        roomService.updateSettings(roomId, currentUser.userId(jwt), request);
        return ResponseEntity.noContent().build();
    }

    /**
     * 직업 배정 설정 변경 (방장만). 성공 시 204.
     * body: {"mode":"CUSTOM","customCompositions":[{"playerCount":6,"roles":["PIRATE_RAIDER", ...]}],"randomCandidates":["PIRATE_PARROT", ...]}
     * 설정 전체를 보낸다. 저장한 설정은 방 단건 조회(GET /{roomId})의 roleSetup으로 보이고, 게임 시작 때 이 설정으로 배정한다.
     * 선택지(직업 목록, 인원수별 추천 구성)는 GET /api/v1/role-setup/options.
     * 실패: 403 방장 아님, 409 게임 중,
     *       400 body 없음 / mode 없음·잘못된 값 / 없는 직업 / 인원수 범위(4~12) 밖 / 커스텀 구성 규칙 위반 / 존재하지 않는 방
     */
    @PutMapping("/{roomId}/role-setup")
    public ResponseEntity<Void> updateRoleSetup(
            @PathVariable Long roomId,
            @AuthenticationPrincipal Jwt jwt,
            @RequestBody(required = false) RoleSetup request
    ) {
        if (request == null) throw new IllegalArgumentException("직업 배정 설정을 보내주세요.");

        roomService.updateRoleSetup(roomId, currentUser.userId(jwt), request);
        return ResponseEntity.noContent().build();
    }

    /**
     * 플레이어 추방 (방장만). 성공 시 204. 추방된 사람은 이 방에 다시 들어올 수 없다.
     * 실패: 403 방장 아님, 409 게임 중 / 대상이 방에 없음, 400 자기 자신 / 존재하지 않는 방
     * (본인 나가기는 DELETE /players/me. Spring은 고정 경로 "me"를 {userId}보다 먼저 고른다)
     */
    @DeleteMapping("/{roomId}/players/{userId}")
    public ResponseEntity<Void> kickPlayer(
            @PathVariable Long roomId,
            @PathVariable Long userId,
            @AuthenticationPrincipal Jwt jwt
    ) {
        roomService.kickPlayer(roomId, currentUser.userId(jwt), userId);
        return ResponseEntity.noContent().build();
    }

    /**
     * 게임 시작 (방장만): 이 방에 게임을 생성한다. 성공하면 방이 IN_GAME이 되고 gameId를 돌려준다.
     * 다른 참가자는 방 조회(GET /api/v1/rooms)의 gameId로 게임 API(/api/v1/games/{gameId})에 접근한다.
     * 실패: 403 방장 아님, 409 이미 게임 중 / 인원 부족 / 준비 안 된 참가자 있음
     */
    @PostMapping("/{roomId}/games")
    public ResponseEntity<StartGameResponse> startGame(
            @PathVariable Long roomId,
            @AuthenticationPrincipal Jwt jwt
    ) {
        return ResponseEntity
                .status(HttpStatus.CREATED)
                .body(roomService.startGame(roomId, currentUser.userId(jwt)));
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

}
