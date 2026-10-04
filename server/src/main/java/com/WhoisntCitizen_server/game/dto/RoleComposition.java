package com.WhoisntCitizen_server.game.dto;

import java.util.List;

/**
 * 한 인원수의 직업 구성. roles는 roles.code 목록이고 같은 직업이 여러 번 들어갈 수 있다.
 * Unity JsonUtility가 Map을 읽지 못해서 인원수별 표는 Map 대신 이 객체의 배열로 주고받는다.
 */
public record RoleComposition(int playerCount, List<String> roles) {
}
