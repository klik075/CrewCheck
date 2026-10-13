package com.WhoisntCitizen_server.game.entity;

/** 사망 원인. 인어 승리(처형) 판정과 밤 결과의 사망 연출(크라켄 컷신 등)에 쓴다. */
public enum DeathCause {
    ATTACK,     // 밤에 해적의 습격
    KRAKEN,     // 밤에 크라켄의 발동
    EXECUTION,  // 낮 투표로 처형
    DISCONNECT  // 연결이 끊겨 사망 처리
}
