package com.WhoisntCitizen_server.lobby.domain.room;

/** 방 상태. 게임이 진행 중인 방에는 입장/퇴장을 막는 데 사용한다. */
public enum RoomStatus {
    WAITING,   // 대기 중 (입장 가능, 게임 시작 가능)
    IN_GAME    // 게임 진행 중
}
