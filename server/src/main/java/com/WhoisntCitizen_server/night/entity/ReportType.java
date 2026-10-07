package com.WhoisntCitizen_server.night.entity;

/** 밤 판정 후 행동자 본인에게만 공개되는 결과의 종류. 능력을 추가할 때 함께 늘린다. */
public enum ReportType {
    FACTION,        // 선장 조사: 대상의 진영
    CORPSE_ROLE,    // 주정뱅이: 시체의 직업
    VISITORS,       // 망루지기: 대상을 방문한 사람
    ACTIONS,        // 앵무새: 대상이 한 행동
    BLOCK,          // 갑판장: 대상을 차단함 (원숭이 갑판장도 같은 결과를 받는다)
    BLOCKED,        // 갑판장에게 차단당해 이번 밤 능력이 무효가 됨 (행동한 본인에게만, targetId 없음)
    VOTE_BAN,       // 요리사: 대상이 다음 투표를 못 하게 함
    VOTE_BANNED,    // 요리사에게 당해 오늘 투표를 못 함 (당한 본인에게만, targetId 없음)
    SEDUCE_SUCCESS, // 세이렌: 대상을 세이렌 팀으로 만듦 (다음 밤은 쉰다)
    SEDUCE_FAIL,    // 세이렌: 유혹할 수 없는 대상이라 실패 (다음 밤에 다시 쓸 수 있다)
    SEDUCED,        // 세이렌에게 유혹당해 세이렌 팀이 됨 (당한 본인에게만, targetId 없음, players = 세이렌 팀 동료)
    KRAKEN_MARK,    // 크라켄: 대상에게 표식을 남김
    KRAKEN_MARKED   // 크라켄의 표식이 남음 (당한 본인에게만, targetId 없음)
}