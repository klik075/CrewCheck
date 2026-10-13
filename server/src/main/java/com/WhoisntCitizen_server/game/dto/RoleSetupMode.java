package com.WhoisntCitizen_server.game.dto;

/** 방장이 고르는 직업 배정 방식. 이름은 API의 roleSetup.mode 값과 같다. */
public enum RoleSetupMode {
    /** 서버의 인원수별 추천 구성표(RoleAssigner.RECOMMENDED)대로 배정한다. */
    RECOMMENDED("추천 구성"),
    /** 방장이 편집한 인원수별 구성표대로 배정한다. 편집하지 않은 인원수는 추천 구성을 쓴다. */
    CUSTOM("커스텀 구성"),
    /** 진영 수는 추천 구성과 같게 두고, 각 진영 안의 직업을 방장이 고른 후보 중에서 무작위로 뽑는다. */
    RANDOM("랜덤 구성");

    private final String label;

    RoleSetupMode(String label) {
        this.label = label;
    }

    /** 방 채팅 안내에 쓰는 이름 */
    public String label() {
        return label;
    }
}
