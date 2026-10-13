package com.WhoisntCitizen_server.game.controller;

import com.WhoisntCitizen_server.game.dto.DaySkipResponse;
import com.WhoisntCitizen_server.game.dto.GameResultResponse;
import com.WhoisntCitizen_server.game.dto.GameStateResponse;
import com.WhoisntCitizen_server.game.dto.MyRoleResponse;
import com.WhoisntCitizen_server.game.service.GameService;
import org.springframework.web.bind.annotation.GetMapping;
import org.springframework.web.bind.annotation.PathVariable;
import org.springframework.web.bind.annotation.PostMapping;
import com.WhoisntCitizen_server.user.service.CurrentUserResolver;
import org.springframework.security.core.annotation.AuthenticationPrincipal;
import org.springframework.security.oauth2.jwt.Jwt;
import org.springframework.web.bind.annotation.RequestMapping;
import org.springframework.web.bind.annotation.RestController;

/**
 * 진행 중인 게임 조회(상태/내 역할/최종 결과).
 * 게임 생성은 방에서 한다: POST /api/v1/rooms/{roomId}/games (RoomController)
 * 로컬 테스트용 생성 API는 DevGameController (local 프로필 전용).
 * 모든 요청은 Authorization: Bearer {accessToken} 이 필요하다.
 * "나"는 토큰으로 판단한다 (CurrentUserResolver: 토큰 sub=memberId -> userId = playerId).
 */
@RestController
@RequestMapping("/api/v1/games")
public class GameController {

    private final GameService gameService;
    private final CurrentUserResolver currentUser;

    public GameController(GameService gameService, CurrentUserResolver currentUser) {
        this.gameService = gameService;
        this.currentUser = currentUser;
    }

    /**
     * 3·5·6·9. 현재 상태 조회 (phase = NIGHT / NIGHT_RESULT / DAY / VOTE / EXECUTION / ENDED)
     * 요청한 플레이어의 접속 시각을 기록한다. 게임 중 이 요청이 오래 없으면 연결이 끊긴 것으로 보고 사망 처리한다.
     */
    @GetMapping("/{gameId}")
    public GameStateResponse state(@PathVariable String gameId,
                                   @AuthenticationPrincipal Jwt jwt) {
        return gameService.getState(gameId, currentUser.userId(jwt));
    }

    /** 2. 내 역할 조회 */
    @GetMapping("/{gameId}/me")
    public MyRoleResponse myRole(@PathVariable String gameId,
                                 @AuthenticationPrincipal Jwt jwt) {
        return gameService.getMyRole(gameId, currentUser.userId(jwt));
    }

    /** 5. 낮 토론 넘기기 (DAY 페이즈에서만). 요청 본문 없음. 살아 있는 전원이 넘기면 바로 투표로 넘어간다 */
    @PostMapping("/{gameId}/day/skip")
    public DaySkipResponse skipDay(@PathVariable String gameId,
                                   @AuthenticationPrincipal Jwt jwt) {
        return gameService.skipDay(gameId, currentUser.userId(jwt));
    }

    /** 8·9. 승리 결과 (종료 전이면 ended=false) */
    @GetMapping("/{gameId}/result")
    public GameResultResponse result(@PathVariable String gameId) {
        return gameService.getResult(gameId);
    }
}
