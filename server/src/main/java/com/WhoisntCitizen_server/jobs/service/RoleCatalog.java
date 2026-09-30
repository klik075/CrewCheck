package com.WhoisntCitizen_server.jobs.service;

import com.WhoisntCitizen_server.jobs.domain.RoleDefinition;
import com.WhoisntCitizen_server.jobs.repository.RoleRepository;
import org.springframework.stereotype.Component;

import java.util.Map;
import java.util.stream.Collectors;

/**
 * 활성화된 직업 정의를 서버 시작 시 한 번만 읽어 캐시한다.
 * 직업 데이터는 게임 중에 바뀌지 않으므로 게임 루프에서 DB를 다시 조회하지 않는다.
 */
@Component
public class RoleCatalog {

    private final Map<String, RoleDefinition> byCode;

    public RoleCatalog(RoleRepository repo) {
        this.byCode = repo.findByEnabledTrueOrderByCodeAsc().stream()
                .map(RoleDefinition::from)
                .collect(Collectors.toUnmodifiableMap(RoleDefinition::code, r -> r));
    }

    public RoleDefinition get(String code) {
        RoleDefinition roleDefinition = byCode.get(code);
        if (roleDefinition == null) {
            throw new IllegalStateException("없는 직업: " + code);
        }
        return roleDefinition;
    }
}
