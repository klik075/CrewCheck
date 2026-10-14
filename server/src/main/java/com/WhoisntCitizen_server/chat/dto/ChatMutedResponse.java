package com.WhoisntCitizen_server.chat.dto;

/**
 * 도배로 채팅이 금지됐을 때의 오류 본문 (POST /api/v1/rooms/{roomId}/messages → 429)
 *  - 금지가 막 시작됨(같은 내용 10번째):
 *    {"code":"CHAT_MUTED","message":"도배로 3분간 채팅이 금지되었습니다.","warning":"같은 내용을 10번 연속으로 보냈습니다. (10/10)","remainingSeconds":180}
 *  - 이미 금지 중:
 *    {"code":"CHAT_MUTED","message":"도배로 채팅이 금지된 상태입니다. 2분 15초 후에 다시 채팅할 수 있습니다.","warning":null,"remainingSeconds":135}
 * 클라이언트는 warning을 주황색, message를 붉은색으로 표시한다. (다른 오류와 같이 code·message가 있어 기존 오류 처리도 그대로 동작)
 */
public record ChatMutedResponse(String code, String message, String warning, long remainingSeconds) {
}
