package com.WhoisntCitizen_server.common.event;

/**
 * 같은 게임의 해적(접선한 앵무새 포함)에게만 보여 줄 시스템 안내. (해적의 공격 대상 선택, 앵무새 접선 등)
 * 게임 모듈이 발행하고 채팅 모듈(ChatNoticeEventListener)이 받아 해적에게만 보이는 type=SYSTEM 메시지로 저장한다.
 * roomId는 로비 방 id 문자열이다. 숫자가 아니면(개발용 POST /api/v1/games 게임 등) 채팅에 남기지 않는다.
 */
public record PirateNoticeEvent(String roomId, String gameId, String message) {
}
