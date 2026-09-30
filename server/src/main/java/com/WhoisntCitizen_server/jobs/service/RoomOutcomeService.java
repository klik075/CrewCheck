package com.WhoisntCitizen_server.jobs.service;

import com.WhoisntCitizen_server.jobs.domain.Faction;
import com.WhoisntCitizen_server.jobs.domain.RoleEntity;
import com.WhoisntCitizen_server.jobs.domain.RoomEntity;
import com.WhoisntCitizen_server.jobs.domain.RoomPhase;
import com.WhoisntCitizen_server.jobs.domain.RoomPlayerEntity;
import com.WhoisntCitizen_server.jobs.repository.RoleRepository;
import java.util.List;
import org.springframework.stereotype.Service;

/**
 * 사망 처리 후 승리 여부를 계산한다. 밤 판정과 포수 낮 사격에서 호출한다.
 * 낮 투표 처형을 추가하면 그 처리 직후에도 check를 호출해야 한다.
 * 목적지 도착 승리는 아직 정의되지 않아 게임 진행 기능에서 추가해야 한다.
 */
@Service
public class RoomOutcomeService {
    private final RoleRepository roles;

    public RoomOutcomeService(RoleRepository roles) {
        this.roles = roles;
    }

    public void check(RoomEntity room, List<RoomPlayerEntity> players) {
        if (room.getPhase() == RoomPhase.GAME_OVER) return;

        long crew = players.stream().filter(RoomPlayerEntity::isAlive)
            .filter(player -> factionOf(player) == Faction.CREW).count();
        long pirates = players.stream().filter(RoomPlayerEntity::isAlive)
            .filter(player -> factionOf(player) == Faction.PIRATE).count();
        if (pirates == 0 && crew > 0) {
            // 살아 있는 해적이 없어졌을 때만 선원 승리다.
            room.finish(Faction.CREW);
        } else if (pirates > 0 && pirates >= crew) {
            // 해적 수가 선원 수 이상이면 승리다. 인원 규칙을 바꾸려면 여기와 테스트를 수정한다.
            room.finish(Faction.PIRATE);
        }
    }

    public Faction factionOf(RoomPlayerEntity player) {
        RoleEntity role = roles.findById(player.getRoleCode())
            .orElseThrow(() -> new IllegalStateException("Assigned role is missing"));
        return role.getFaction();
    }
}
