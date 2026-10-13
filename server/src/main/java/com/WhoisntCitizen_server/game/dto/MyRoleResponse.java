package com.WhoisntCitizen_server.game.dto;

import com.WhoisntCitizen_server.game.entity.Game;
import com.WhoisntCitizen_server.game.entity.GamePlayer;
import com.WhoisntCitizen_server.game.entity.Team;
import com.WhoisntCitizen_server.jobs.domain.ActionCode;
import com.WhoisntCitizen_server.jobs.domain.Faction;
import com.WhoisntCitizen_server.jobs.domain.RoleDefinition;

import java.util.List;

/**
 * 2. 내 역할 조회. 해적 진영에게는 알고 있는 동료 목록도 알려준다.
 * role = roles.code (예: PIRATE_RAIDER), actionCode가 null이면 능력 없음.
 * 직업 정보는 본인에게 보이는 직업(shownRole) 기준이다. 원숭이에게는 위장 직업을 보여 주고 실제 직업은 숨긴다.
 * remainingUses: 능력의 남은 사용 횟수. 무제한이거나 능력이 없으면 -1.
 * mafiaTeammateIds: 해적끼리는 처음부터 서로 보이고, 앵무새는 접선한 뒤에만 해적과 서로 보인다.
 * contacted: 앵무새가 해적과 접선했는지. 앵무새가 아니면 false.
 * team: 지금 속한 팀. 세이렌에게 유혹당하면 직업은 그대로이고 SIREN이 된다. 원숭이는 위장 직업 기준이다(GamePlayer.getShownTeam).
 * sirenTeamIds: 세이렌 팀이면 다른 세이렌 팀원(사망자 포함). 아니면 빈 목록.
 * krakenMarkIds: 크라켄이면 표식을 남긴 생존자. 아니면 빈 목록.
 * abilityAvailable: 이번 밤(밤이 아니면 다음 밤)에 능력을 쓸 수 있는지. 세이렌은 유혹에 성공한 다음 밤에 false.
 */
public record MyRoleResponse(
        Long playerId,
        String role,
        String roleName,
        Faction faction,
        ActionCode actionCode,
        int remainingUses,
        boolean alive,
        List<Long> mafiaTeammateIds,
        boolean contacted,
        Team team,
        List<Long> sirenTeamIds,
        List<Long> krakenMarkIds,
        boolean abilityAvailable
) {
    public static MyRoleResponse of(Game game, GamePlayer me) {
        RoleDefinition shown = me.getShownRole();
        ActionCode code = shown.actionCode();
        int remainingUses = code == null ? -1 : me.remainingUses(code);
        // isPirate()로 거르면 접선 전 앵무새가 드러나므로 접선 규칙이 들어간 knownPirateAllies를 쓴다.
        List<Long> mafiaTeammateIds = ids(game.knownPirateAllies(me));
        List<Long> sirenTeamIds = ids(game.knownSirenTeam(me));
        List<Long> krakenMarkIds = me.isKraken() ? game.aliveKrakenMarks(me) : List.of();
        return new MyRoleResponse(me.getPlayerId(), shown.code(), shown.name(),
                shown.faction(), code, remainingUses, me.isAlive(), mafiaTeammateIds, me.isContacted(),
                me.getShownTeam(), sirenTeamIds, krakenMarkIds, game.canUseAbilityTonight(me));
    }

    private static List<Long> ids(List<GamePlayer> players) {
        return players.stream().map(GamePlayer::getPlayerId).toList();
    }
}
