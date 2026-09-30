package com.WhoisntCitizen_server.night.entity;

import com.WhoisntCitizen_server.jobs.domain.ActionCode;

/**
 * 밤에 제출된 행동 한 건. 판정 전까지는 같은 행동자의 새 제출이 이전 제출을 덮어쓴다.
 * code는 제출 당시 사용한 능력이다. 원숭이는 위장 직업의 능력을 쓰므로 판정 때 직업에서 다시 계산하지 않는다.
 */
public record NightAction(Long actorId, ActionCode code, Long targetId) {
}