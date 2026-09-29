package com.sparta.unityaitestproject_server.game.controller;

import com.sparta.unityaitestproject_server.game.dto.GameResultResponse;
import com.sparta.unityaitestproject_server.game.dto.GameStateResponse;
import com.sparta.unityaitestproject_server.game.dto.MyRoleResponse;
import com.sparta.unityaitestproject_server.game.dto.StartGameRequest;
import com.sparta.unityaitestproject_server.game.dto.StartGameResponse;
import com.sparta.unityaitestproject_server.game.service.GameService;
import jakarta.validation.Valid;
import org.springframework.http.HttpStatus;
import org.springframework.http.ResponseEntity;
import org.springframework.web.bind.annotation.GetMapping;
import org.springframework.web.bind.annotation.PathVariable;
import org.springframework.web.bind.annotation.PostMapping;
import org.springframework.web.bind.annotation.RequestBody;
import org.springframework.web.bind.annotation.RequestHeader;
import org.springframework.web.bind.annotation.RequestMapping;
import org.springframework.web.bind.annotation.ResponseStatus;
import org.springframework.web.bind.annotation.RestController;

/**
 * 게임 자체(생성/상태/내 역할/최종 결과).
 * X-Player-Id 헤더는 로그인 기능이 붙기 전 임시 식별 수단이다. 인증 도입 후 토큰에서 꺼내도록 교체.
 */
@RestController
@RequestMapping("/api/games")
public class GameController {

    private final GameService gameService;

    public GameController(GameService gameService) {
        this.gameService = gameService;
    }

    /** 1. 게임 시작 (+ 2. 역할 배정, 첫 밤 진입) */
    @PostMapping
    public ResponseEntity<StartGameResponse> start(@Valid @RequestBody StartGameRequest request) {
        return ResponseEntity
                .status(HttpStatus.CREATED)
                .body(gameService.startGame(request));
    }

    /** 3·5·6·9. 현재 상태 조회 (phase = NIGHT / NIGHT_RESULT / DAY / VOTE / EXECUTION / ENDED) */
    @GetMapping("/{gameId}")
    public GameStateResponse state(@PathVariable String gameId) {
        return gameService.getState(gameId);
    }

    /** 2. 내 역할 조회 */
    @GetMapping("/{gameId}/me")
    public MyRoleResponse myRole(@PathVariable String gameId,
                                 @RequestHeader("X-Player-Id") Long playerId) {
        return gameService.getMyRole(gameId, playerId);
    }

    /** 8·9. 승리 결과 (종료 전이면 ended=false) */
    @GetMapping("/{gameId}/result")
    public GameResultResponse result(@PathVariable String gameId) {
        return gameService.getResult(gameId);
    }
}
