package com.WhoisntCitizen_server.lobby.dto;

import lombok.Getter;
import lombok.NoArgsConstructor;

/**
 * 준비 상태 변경 요청 body (PUT /api/v1/rooms/{roomId}/players/me/ready).
 *   {"ready": true}  → 준비
 *   {"ready": false} → 준비 취소
 *
 * boolean이 아니라 Boolean으로 받는 이유:
 *   boolean이면 {}처럼 ready가 빠진 요청이 false로 읽혀 준비가 조용히 해제된다.
 *   Boolean이면 null로 들어오므로 컨트롤러에서 400으로 거절할 수 있다.
 */
@Getter
@NoArgsConstructor
public class SetReadyRequestDto {
    private Boolean ready;
}
