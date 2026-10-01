package com.WhoisntCitizen_server.game.controller;

import com.WhoisntCitizen_server.game.dto.StartGameRequest;
import com.WhoisntCitizen_server.game.dto.StartGameResponse;
import com.WhoisntCitizen_server.game.service.GameService;
import jakarta.validation.Valid;
import org.springframework.context.annotation.Profile;
import org.springframework.http.HttpStatus;
import org.springframework.http.ResponseEntity;
import org.springframework.web.bind.annotation.PostMapping;
import org.springframework.web.bind.annotation.RequestBody;
import org.springframework.web.bind.annotation.RequestMapping;
import org.springframework.web.bind.annotation.RestController;

/**
 * 개발용 게임 생성 API (local 프로필에서만 등록된다).
 * 로그인/방 없이 플레이어 목록을 직접 보내 게임 규칙을 빠르게 테스트할 때 사용한다. (Postman 시나리오)
 * 실제 게임 생성은 POST /api/v1/rooms/{roomId}/games 로만 한다.
 * prod 프로필에서는 이 컨트롤러가 없어서 404가 된다.
 */
@Profile("local")
@RestController
@RequestMapping("/api/v1/games")
public class DevGameController {

    private final GameService gameService;

    public DevGameController(GameService gameService) {
        this.gameService = gameService;
    }

    /** 1. 게임 시작 (+ 2. 역할 배정, 첫 밤 진입) */
    @PostMapping
    public ResponseEntity<StartGameResponse> start(@Valid @RequestBody StartGameRequest request) {
        return ResponseEntity
                .status(HttpStatus.CREATED)
                .body(gameService.startGame(request));
    }
}
