package com.WhoisntCitizen_server.game.controller;

import com.WhoisntCitizen_server.game.dto.GameResultResponse;
import com.WhoisntCitizen_server.game.dto.GameStateResponse;
import com.WhoisntCitizen_server.game.dto.MyRoleResponse;
import com.WhoisntCitizen_server.game.service.GameService;
import org.springframework.web.bind.annotation.GetMapping;
import org.springframework.web.bind.annotation.PathVariable;
import org.springframework.web.bind.annotation.RequestHeader;
import org.springframework.web.bind.annotation.RequestMapping;
import org.springframework.web.bind.annotation.RestController;

/**
 * 진행 중인 게임 조회(상태/내 역할/최종 결과).
 * 게임 생성은 방에서 한다: POST /api/v1/rooms/{roomId}/games (RoomController)
 * 로컬 테스트용 생성 API는 DevGameController (local 프로필 전용).
 * X-Player-Id 헤더는 로그인 기능이 붙기 전 임시 식별 수단이다. 인증 도입 후 토큰에서 꺼내도록 교체.
 */
@RestController
@RequestMapping("/api/v1/games")
public class GameController {

    private final GameService gameService;

    public GameController(GameService gameService) {
        this.gameService = gameService;
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
