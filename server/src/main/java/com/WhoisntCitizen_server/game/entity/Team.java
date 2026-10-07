package com.WhoisntCitizen_server.game.entity;

/**
 * 게임 안에서 플레이어가 속한 팀. 처음에는 직업의 진영(Faction)을 따르고, 세이렌에게 유혹당하면 SIREN으로 바뀐다.
 * 직업과 능력은 바뀌지 않으므로 직업 정보(roles.faction)와 따로 둔다.
 */
public enum Team {
    CREW,       // 선원 팀 (유혹되지 않은 선원 진영)
    PIRATE,     // 해적 팀 (해적 진영. 유혹되지 않는다)
    SIREN,      // 세이렌 팀 (세이렌 + 유혹당한 선원 진영·인어)
    NEUTRAL     // 혼자 이기는 제3 세력 (크라켄, 유령 선장, 유혹되지 않은 인어)
}
