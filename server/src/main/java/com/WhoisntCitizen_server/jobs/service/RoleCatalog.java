package com.WhoisntCitizen_server.jobs.service;

import com.WhoisntCitizen_server.jobs.domain.RoleDefinition;
import com.WhoisntCitizen_server.jobs.repository.RoleRepository;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.stereotype.Component;

import java.util.Collection;
import java.util.Collections;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;
import java.util.Optional;

/**
 * 활성화된 직업 정의를 서버 시작 시 한 번만 읽어 캐시한다.
 * 직업 데이터는 게임 중에 바뀌지 않으므로 게임 루프에서 DB를 다시 조회하지 않는다.
 */
@Component
public class RoleCatalog {

    private final Map<String, RoleDefinition> byCode;

    @Autowired
    public RoleCatalog(RoleRepository repo) {
        this(repo.findByEnabledTrueOrderByCodeAsc().stream().map(RoleDefinition::from).toList());
    }

    /** DB 없이 직업 목록으로 만든다. (테스트용) 넘긴 순서를 그대로 유지한다. */
    public RoleCatalog(Collection<RoleDefinition> roles) {
        Map<String, RoleDefinition> map = new LinkedHashMap<>();
        roles.forEach(role -> map.put(role.code(), role));
        this.byCode = Collections.unmodifiableMap(map);
    }

    /** 서버가 정한 직업 코드용. 없으면 설정 오류라 IllegalStateException */
    public RoleDefinition get(String code) {
        RoleDefinition roleDefinition = byCode.get(code);
        if (roleDefinition == null) {
            throw new IllegalStateException("없는 직업: " + code);
        }
        return roleDefinition;
    }

    /** 방장이 보낸 값처럼 없을 수도 있는 직업 코드용 */
    public Optional<RoleDefinition> find(String code) {
        return Optional.ofNullable(byCode.get(code));
    }

    /** 활성화된 직업 전체 (DB에서 읽은 경우 code 오름차순) */
    public List<RoleDefinition> all() {
        return List.copyOf(byCode.values());
    }
}
