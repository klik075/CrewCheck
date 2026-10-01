package com.WhoisntCitizen_server.night.entity;

/** 밤 판정 후 행동자 본인에게만 공개되는 결과의 종류. 능력을 추가할 때 함께 늘린다. */
public enum ReportType {
    FACTION,        // 선장 조사: 대상의 진영
    CORPSE_ROLE,    // 주정뱅이: 시체의 직업
    VISITORS,       // 망루지기: 대상을 방문한 사람
    ACTIONS         // 앵무새: 대상이 한 행동
}