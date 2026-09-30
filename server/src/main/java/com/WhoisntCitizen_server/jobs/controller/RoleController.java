package com.WhoisntCitizen_server.jobs.controller;

import com.WhoisntCitizen_server.jobs.domain.Faction;
import com.WhoisntCitizen_server.jobs.dto.RoleApiDtos.ActionResponse;
import com.WhoisntCitizen_server.jobs.dto.RoleApiDtos.MyRoleResponse;
import com.WhoisntCitizen_server.jobs.dto.RoleApiDtos.PirateAttackTargetResponse;
import com.WhoisntCitizen_server.jobs.dto.RoleApiDtos.RoleListResponse;
import com.WhoisntCitizen_server.jobs.dto.RoleApiDtos.ReportsResponse;
import com.WhoisntCitizen_server.jobs.dto.RoleApiDtos.SubmitActionRequest;
import com.WhoisntCitizen_server.jobs.service.RoleService;
import jakarta.validation.Valid;
import org.springframework.http.HttpStatus;
import org.springframework.security.core.annotation.AuthenticationPrincipal;
import org.springframework.security.oauth2.jwt.Jwt;
import org.springframework.web.bind.annotation.GetMapping;
import org.springframework.web.bind.annotation.PathVariable;
import org.springframework.web.bind.annotation.PostMapping;
import org.springframework.web.bind.annotation.RequestBody;
import org.springframework.web.bind.annotation.RequestMapping;
import org.springframework.web.bind.annotation.RequestParam;
import org.springframework.web.bind.annotation.ResponseStatus;
import org.springframework.web.bind.annotation.RestController;
import org.springframework.web.server.ResponseStatusException;


/**
 * Unity가 호출하는 직업 API의 HTTP 진입점.
 * 요청 해석과 인증 사용자 확인만 맡고 게임 규칙은 RoleService에서 처리한다.
 * 공개 URL의 gamesId는 현재 내부 RoomEntity.id와 동일하다. DB 이름을 바꿀 필요는 없다.
 */
@RestController
@RequestMapping("/api/v1")
public class RoleController {
    private final RoleService roleService;

    public RoleController(RoleService roleService) {
        this.roleService = roleService;
    }

    @GetMapping("/roles")
    public RoleListResponse roles(@RequestParam(required = false) Faction faction) {
        // 공개 정보이므로 로그인 없이 조회할 수 있다. faction이 없으면 전체 직업을 반환한다.
        return roleService.listRoles(faction);
    }

    @GetMapping("/games/{gamesId}/me/role")
    public MyRoleResponse myRole(@PathVariable Long gamesId, @AuthenticationPrincipal Jwt jwt) {
        // 다른 참가자의 직업을 조회하지 못하도록 userId를 요청값이 아닌 JWT에서 가져온다.
        return roleService.getMyRole(gamesId, userId(jwt));
    }

    @GetMapping("/games/{gamesId}/me/reports")
    public ReportsResponse myReports(@PathVariable Long gamesId, @AuthenticationPrincipal Jwt jwt) {
        return roleService.getMyReports(gamesId, userId(jwt));
    }

    @GetMapping("/games/{gamesId}/pirate-attack")
    public PirateAttackTargetResponse pirateAttack(@PathVariable Long gamesId,
                                                    @AuthenticationPrincipal Jwt jwt) {
        // 해적에게만 이번 밤의 공유 공격 대상을 보여 준다.
        return roleService.getPirateAttackTarget(gamesId, userId(jwt));
    }

    @PostMapping("/games/{gamesId}/actions")
    @ResponseStatus(HttpStatus.ACCEPTED)
    public ActionResponse submitAction(@PathVariable Long gamesId,
                                       @AuthenticationPrincipal Jwt jwt,
                                       @Valid @RequestBody SubmitActionRequest request) {
        return roleService.submitAction(gamesId, userId(jwt), request);
    }

    private static Long userId(Jwt jwt) {
        // 인증 서버는 JWT의 sub에 숫자 userId를 넣어야 한다.
        try {
            return Long.valueOf(jwt.getSubject());
        } catch (NumberFormatException e) {
            throw new ResponseStatusException(HttpStatus.UNAUTHORIZED, "Invalid user ID in token", e);
        }
    }
}
