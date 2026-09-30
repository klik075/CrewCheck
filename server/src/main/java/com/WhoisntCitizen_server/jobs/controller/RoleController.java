package com.WhoisntCitizen_server.jobs.controller;

import com.WhoisntCitizen_server.jobs.domain.Faction;
import com.WhoisntCitizen_server.jobs.dto.RoleApiDtos.RoleListResponse;
import com.WhoisntCitizen_server.jobs.service.RoleService;
import org.springframework.web.bind.annotation.GetMapping;
import org.springframework.web.bind.annotation.RequestMapping;
import org.springframework.web.bind.annotation.RequestParam;
import org.springframework.web.bind.annotation.RestController;

/**
 * 공개 직업 정보 API.
 * "이 게임에서 내 직업" 조회는 게임 상태에 속하므로 GameController(/api/games/{gameId}/me)가 담당한다.
 */
@RestController
@RequestMapping("/api/v1")
public class RoleController {
    private final RoleService roleService;

    public RoleController(RoleService roleService) {
        this.roleService = roleService;
    }

    /** 활성화된 직업 목록. faction(CREW/PIRATE/NEUTRAL)을 주면 해당 진영만 반환한다. */
    @GetMapping("/roles")
    public RoleListResponse roles(@RequestParam(required = false) Faction faction) {
        return roleService.listRoles(faction);
    }
}
