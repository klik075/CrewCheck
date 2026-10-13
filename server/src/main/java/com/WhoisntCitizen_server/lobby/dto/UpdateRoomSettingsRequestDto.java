package com.WhoisntCitizen_server.lobby.dto;

import lombok.Getter;
import lombok.NoArgsConstructor;

/**
 * 방 설정 변경 요청 (PUT /api/v1/rooms/{roomId}/settings, 방장만).
 *
 * 설정 전체를 보내는 방식(PUT)이다. 보낸 값이 그대로 방의 새 설정이 된다.
 *   Unity JsonUtility는 int/bool 필드를 빼고 보낼 수 없어서 "일부만 보내기(PATCH)"가 맞지 않는다.
 *
 * 요청 예
 *   {"title":"초보만","maxPlayers":10,"privateRoom":true,"password":"0427"}
 *
 * 필드 이름은 CreateRoomRequestDto, Unity UpdateRoomSettingsRequest와 같아야 한다.
 *
 * maxPlayers, privateRoom을 Integer/Boolean으로 받는 이유
 *   int/boolean이면 키를 빠뜨렸을 때 0/false로 들어온다. 특히 privateRoom이 빠지면
 *   비밀방이 조용히 공개방으로 바뀌어 버린다. 그래서 null(키 없음)을 받아 400으로 막는다.
 */
@Getter
@NoArgsConstructor
public class UpdateRoomSettingsRequestDto {

    /** 방 제목. 바꾸지 않으려면 현재 제목을 그대로 보낸다. */
    private String title;

    /** 최대 인원 (4~12, 현재 인원 이상). */
    private Integer maxPlayers;

    /** 비밀방 여부. (isPrivate로 지으면 JSON 키가 "private"이 되므로 privateRoom) */
    private Boolean privateRoom;

    /**
     * 비밀번호. 클라이언트는 현재 비밀번호를 모르므로(응답에 절대 넣지 않음) 아래처럼 해석한다.
     *   - 공개방으로 설정: 무시한다. (저장된 비밀번호는 지워진다)
     *   - 공개방 → 비밀방: 필수. 숫자 4자리 이상.
     *   - 비밀방 → 비밀방: 비어 있으면(null/"") 기존 비밀번호 유지, 값이 있으면 새 비밀번호로 교체.
     * Unity JsonUtility는 null 문자열을 ""로 보내므로 ""도 "비어 있음"으로 본다.
     */
    private String password;

    /** 코드에서 직접 만들 때 사용 (테스트 등). Jackson은 static 메서드를 쓰지 않으므로 JSON 변환에는 영향이 없다. */
    public static UpdateRoomSettingsRequestDto of(String title, Integer maxPlayers, Boolean privateRoom, String password) {
        UpdateRoomSettingsRequestDto dto = new UpdateRoomSettingsRequestDto();
        dto.title = title;
        dto.maxPlayers = maxPlayers;
        dto.privateRoom = privateRoom;
        dto.password = password;
        return dto;
    }
}
