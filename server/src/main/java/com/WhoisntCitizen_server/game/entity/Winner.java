package com.WhoisntCitizen_server.game.entity;

/**
 * 게임에서 이긴 쪽. 팀 승리(CREW, PIRATE, SIREN)는 그 팀 전원(사망자 포함)이,
 * 단독 승리(KRAKEN, GHOST_CAPTAIN, MERMAID)는 조건을 채운 그 플레이어만 이긴다. 실제로 이긴 사람은 Game.winnerIds.
 */
public enum Winner {
    CREW,
    PIRATE,
    SIREN,
    KRAKEN,
    GHOST_CAPTAIN,
    MERMAID
}
