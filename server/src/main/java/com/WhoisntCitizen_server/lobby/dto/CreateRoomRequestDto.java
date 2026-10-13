package com.WhoisntCitizen_server.lobby.dto;

import lombok.Getter;
import lombok.NoArgsConstructor;

/**
 * 방 생성 요청 (POST /api/v1/rooms).
 * 방장 id는 토큰(JWT)에서 꺼내므로 요청 body에는 받지 않는다.
 *
 * 요청 예
 *   공개방: {"title":"초보만","maxPlayers":8}
 *   비밀방: {"title":"친구만","maxPlayers":8,"privateRoom":true,"password":"0427"}
 */
@Getter
@NoArgsConstructor
public class CreateRoomRequestDto {
    private String title;
    private int maxPlayers;

    // ---------- 비밀방 설정 ----------

    /**
     * 비밀방 여부. 보내지 않으면 false(공개방).
     * 이름을 isPrivate로 하면 Lombok 게터가 isPrivate()가 되어 Jackson이 JSON 키를 "private"으로 인식한다.
     * 그래서 privateRoom으로 짓는다. (Unity CreateRoomRequest 필드명과 같아야 함)
     */
    private boolean privateRoom;

    /**
     * 비밀번호. 숫자만, 최소 4자리(최대 길이 제한 없음). 검증은 RoomService.createRoom에서 한다.
     * 공개방(privateRoom=false)이면 값이 와도 무시한다.
     */
    private String password;
}
