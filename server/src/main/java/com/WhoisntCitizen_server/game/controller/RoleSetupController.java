package com.WhoisntCitizen_server.game.controller;

import com.WhoisntCitizen_server.game.dto.RoleSetupOptionsResponse;
import com.WhoisntCitizen_server.game.service.RoleAssigner;
import com.WhoisntCitizen_server.jobs.dto.RoleApiDtos.RoleView;
import org.springframework.web.bind.annotation.GetMapping;
import org.springframework.web.bind.annotation.RequestMapping;
import org.springframework.web.bind.annotation.RestController;

/**
 * 직업 배정 설정 화면용 정보. 방의 설정 변경은 PUT /api/v1/rooms/{roomId}/role-setup (RoomController)이 한다.
 * Authorization: Bearer {accessToken} 이 필요하다.
 */
@RestController
@RequestMapping("/api/v1/role-setup")
public class RoleSetupController {

    private final RoleAssigner roleAssigner;

    public RoleSetupController(RoleAssigner roleAssigner) {
        this.roleAssigner = roleAssigner;
    }

    /** 인원 범위, 고를 수 있는 직업, 랜덤 후보, 인원수별 추천 구성 */
    @GetMapping("/options")
    public RoleSetupOptionsResponse options() {
        return new RoleSetupOptionsResponse(
                RoleAssigner.MIN_PLAYERS,
                RoleAssigner.MAX_PLAYERS,
                roleAssigner.roles().stream().map(RoleView::from).toList(),
                roleAssigner.specialRoleCodes(),
                roleAssigner.recommendedCompositions());
    }
}
