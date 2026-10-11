package com.WhoisntCitizen_server.lobby.event;

/**
 * 방에서 게임을 시작하기 직전에 발행되는 이벤트. (RoomService.startGame, 방 잠금 안)
 * 게임 생성(GameService.startGame)이 시작하자마자 채팅 안내(역할 배정, 첫 밤)를 남기므로 그보다 먼저 발행한다.
 * 채팅 모듈이 받아 "여기부터가 게임 중 메시지"라는 표시를 남기고, 게임이 끝나 방이 대기 상태로 돌아가면
 * (RoomGameFinishedEvent) 그 뒤의 메시지를 지운다.
 * 게임 생성이 실패하면 표시만 남는데, 다음 시작 때 덮어쓰므로 문제없다.
 */
public record RoomGameStartingEvent(Long roomId) {
}
