package com.WhoisntCitizen_server.jobs.repository;

import com.WhoisntCitizen_server.jobs.domain.Faction;
import com.WhoisntCitizen_server.jobs.domain.RoleEntity;
import java.util.List;
import org.springframework.data.jpa.repository.JpaRepository;

public interface RoleRepository extends JpaRepository<RoleEntity, String> {
    List<RoleEntity> findByEnabledTrueOrderByCodeAsc();
    List<RoleEntity> findByFactionAndEnabledTrueOrderByCodeAsc(Faction faction);
}
