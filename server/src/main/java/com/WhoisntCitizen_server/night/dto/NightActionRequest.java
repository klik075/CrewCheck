package com.WhoisntCitizen_server.night.dto;

import com.WhoisntCitizen_server.jobs.domain.ActionCode;

/**
 * 3. 밤 능력 사용 요청.
 * targetId: 대상. 대상이 없는 능력(크라켄 발동 KRAKEN_STRIKE)이면 비워 두거나 0을 보내도 된다(무시한다).
 * actionCode: 고른 능력. 비우면(null 또는 "") 직업의 기본 능력이다. 지금은 크라켄만 KRAKEN_STRIKE를 고를 수 있다.
 * Unity JsonUtility는 null 문자열을 ""로 보내므로 actionCode는 문자열로 받아 직접 바꾼다.
 */
public record NightActionRequest(Long targetId, String actionCode) {

    public NightActionRequest(Long targetId) {
        this(targetId, null);
    }

    /** 고른 능력. 비어 있으면 null(기본 능력). 없는 이름이면 400. */
    public ActionCode actionCodeOrNull() {
        if (actionCode == null || actionCode.isBlank()) {
            return null;
        }
        try {
            return ActionCode.valueOf(actionCode.trim());
        } catch (IllegalArgumentException e) {
            throw new IllegalArgumentException("없는 능력입니다: " + actionCode);
        }
    }
}
