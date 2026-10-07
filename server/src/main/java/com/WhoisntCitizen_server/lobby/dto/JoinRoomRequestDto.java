package com.WhoisntCitizen_server.lobby.dto;

import lombok.Getter;
import lombok.NoArgsConstructor;

/**
 * 방 입장 요청 body (POST /api/v1/rooms/{roomId}/players).
 *
 * body는 선택이다.
 *   - 공개방: body 없이 보내도 된다. (기존 클라이언트와 호환)
 *   - 비밀방: {"password":"0427"} 처럼 비밀번호를 보내야 한다. 없거나 틀리면 403 WRONG_ROOM_PASSWORD.
 */
@Getter
@NoArgsConstructor
public class JoinRoomRequestDto {
    private String password; // 숫자 타입(int)으로 받으면 "0427"이 427이 되어 앞자리 0이 사라지므로 문자열로 받는다
}
