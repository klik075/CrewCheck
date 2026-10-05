package com.WhoisntCitizen_server.support;

import com.WhoisntCitizen_server.game.service.RoleAssigner;
import com.WhoisntCitizen_server.jobs.domain.ActionCode;
import com.WhoisntCitizen_server.jobs.domain.Faction;
import com.WhoisntCitizen_server.jobs.domain.RoleDefinition;
import com.WhoisntCitizen_server.jobs.service.RoleCatalog;

import java.util.List;
import java.util.Random;

/** 테스트용 직업 9종(Flyway V2·V4와 같은 값)과 DB 없이 만든 RoleCatalog·RoleAssigner */
public final class TestRoles {

    public static final RoleDefinition RAIDER =
            new RoleDefinition("PIRATE_RAIDER", "해적", Faction.PIRATE, ActionCode.SELECT_ATTACK_TARGET);
    public static final RoleDefinition PARROT =
            new RoleDefinition("PIRATE_PARROT", "앵무새", Faction.PIRATE, ActionCode.WATCH_ACTION);
    public static final RoleDefinition CAPTAIN =
            new RoleDefinition("CREW_CAPTAIN", "선장", Faction.CREW, ActionCode.INVESTIGATE_FACTION);
    public static final RoleDefinition DOCTOR =
            new RoleDefinition("CREW_DOCTOR", "선의", Faction.CREW, ActionCode.PROTECT);
    public static final RoleDefinition LOOKOUT =
            new RoleDefinition("CREW_LOOKOUT", "망루지기", Faction.CREW, ActionCode.WATCH_VISITORS);
    public static final RoleDefinition BOATSWAIN =
            new RoleDefinition("CREW_BOATSWAIN", "갑판장", Faction.CREW, ActionCode.BLOCK);
    public static final RoleDefinition DRUNK =
            new RoleDefinition("CREW_DRUNK", "주정뱅이", Faction.CREW, ActionCode.READ_CORPSE_ROLE);
    public static final RoleDefinition MONKEY =
            new RoleDefinition("CREW_MONKEY", "원숭이", Faction.CREW, null);
    public static final RoleDefinition SAILOR =
            new RoleDefinition("CREW_SAILOR", "선원", Faction.CREW, null);

    /** DB(RoleRepository)가 돌려주는 순서와 같은 code 오름차순 */
    public static final List<RoleDefinition> ALL = List.of(
            BOATSWAIN, CAPTAIN, DOCTOR, DRUNK, LOOKOUT, MONKEY, SAILOR, PARROT, RAIDER);

    private TestRoles() {}

    public static RoleCatalog catalog() {
        return new RoleCatalog(ALL);
    }

    public static RoleAssigner assigner(long seed) {
        return new RoleAssigner(new Random(seed), catalog());
    }
}
