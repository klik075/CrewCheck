package com.WhoisntCitizen_server.jobs.service;

import com.WhoisntCitizen_server.jobs.domain.Faction;
import com.WhoisntCitizen_server.jobs.domain.RoleEntity;
import com.WhoisntCitizen_server.jobs.dto.RoleApiDtos.RoleListResponse;
import com.WhoisntCitizen_server.jobs.dto.RoleApiDtos.RoleView;
import com.WhoisntCitizen_server.jobs.repository.RoleRepository;
import org.springframework.stereotype.Service;
import org.springframework.transaction.annotation.Transactional;

import java.util.List;

/**
 * 공개 직업 목록 조회.
 * 게임 중 직업 배정/능력 처리는 game 모듈이 RoleCatalog(캐시)를 통해 수행한다.
 */
@Service
public class RoleService {
    private final RoleRepository roles;

    public RoleService(RoleRepository roles) {
        this.roles = roles;
    }

    // readOnly = true: 읽기만 하는 트랜잭션. 변경 감지를 생략해 조금 더 가볍다.
    @Transactional(readOnly = true)
    public RoleListResponse listRoles(Faction faction) {
        // enabled=false인 보류 직업은 기존 DB 참조를 위해 남겨 두되 목록에는 보이지 않는다.
        List<RoleEntity> result = faction == null
                ? roles.findByEnabledTrueOrderByCodeAsc()
                : roles.findByFactionAndEnabledTrueOrderByCodeAsc(faction);
        return new RoleListResponse(result.stream().map(RoleView::from).toList());
    }
}
