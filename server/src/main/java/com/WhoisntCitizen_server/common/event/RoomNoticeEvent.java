package com.WhoisntCitizen_server.common.event;

/**
 * 방 채팅창에 시스템 메시지로 보여 줄 안내 문장. (게임 페이즈 전환, 밤/처형 결과, 승리, 대기실 복귀, 전적 저장 등)
 * 발행하는 쪽(game, lobby, member)은 채팅을 모르고 이 이벤트만 발행한다.
 * 채팅 모듈(ChatNoticeEventListener)이 받아 해당 방에 type=SYSTEM 메시지로 저장한다.
 * roomId는 로비 방 id 문자열이다. 숫자가 아니면(개발용 POST /api/v1/games 게임 등) 채팅에 남기지 않는다.
 */
public record RoomNoticeEvent(String roomId, String message) {

    public static RoomNoticeEvent of(Long roomId, String message) {
        return new RoomNoticeEvent(String.valueOf(roomId), message);
    }
}
