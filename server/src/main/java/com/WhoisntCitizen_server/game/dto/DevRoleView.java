package com.WhoisntCitizen_server.game.dto;

import com.WhoisntCitizen_server.game.entity.GamePlayer;

/**
 * 개발용(local 전용) 실제 직업 조회 응답 한 줄. 원숭이처럼 본인에게 보이는 직업(shownRole)과
 * 실제 직업(role)이 다른 경우를 Postman 테스트에서 확인하기 위해 쓴다. 실제 게임 클라이언트는 쓰지 않는다.
 */
public record DevRoleView(Long playerId, String nickname, String role, String shownRole, boolean alive, boolean contacted) {

    public static DevRoleView from(GamePlayer p) {
        return new DevRoleView(p.getPlayerId(), p.getNickname(), p.getRole().code(),
                p.getShownRole().code(), p.isAlive(), p.isContacted());
    }
}
