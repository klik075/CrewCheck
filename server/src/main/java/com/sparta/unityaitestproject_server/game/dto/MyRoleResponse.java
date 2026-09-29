package com.sparta.unityaitestproject_server.game.dto;

import com.sparta.unityaitestproject_server.game.entity.Role;
import com.sparta.unityaitestproject_server.game.entity.Team;

import java.util.List;

/** 2. 내 역할 조회. 마피아에게는 동료 마피아 목록도 알려준다. */
public record MyRoleResponse(Long playerId, Role role, Team team, boolean alive, List<Long> mafiaTeammateIds) {
}
