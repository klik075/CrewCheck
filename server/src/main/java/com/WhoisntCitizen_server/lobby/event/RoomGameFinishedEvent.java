package com.WhoisntCitizen_server.lobby.event;

/**
 * 게임이 끝나 방이 대기 상태(WAITING)로 돌아갔을 때 발행되는 이벤트.
 * (RoomService.returnToWaiting: 정상 종료 / recoverIfOrphaned: 서버 재시작 등으로 게임이 사라진 경우)
 * 채팅 모듈이 받아 게임 중에 오간 메시지(게임 시작 표시 이후)를 지운다.
 * "대기실로 돌아왔습니다" 안내(RoomNoticeEvent)보다 먼저 발행해야 그 안내는 지워지지 않고 남는다.
 */
public record RoomGameFinishedEvent(Long roomId) {
}
