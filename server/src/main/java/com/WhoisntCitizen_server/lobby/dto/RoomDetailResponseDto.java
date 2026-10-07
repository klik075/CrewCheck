package com.WhoisntCitizen_server.lobby.dto;

import com.WhoisntCitizen_server.lobby.domain.room.Room;
import lombok.Getter;

import java.util.List;

/**
 * 방 단건 조회 응답 (GET /api/v1/rooms/{roomId}).
 * 게임 대기실에서 주기적으로 조회해 참가자 목록을 갱신하고,
 * status가 IN_GAME으로 바뀌면 gameId로 게임 화면에 들어간다.
 *
 * 구성 = RoomResponseDto의 공통 필드 + 참가자 명단(players)
 *
 *   공통 필드는 부모(RoomResponseDto)에 선언·매핑되어 있으므로 여기서는 players만 다룬다.
 *   방 공통 필드를 추가할 때는 이 클래스가 아니라 RoomResponseDto를 수정한다.
 *
 * 목록 API(GET /api/v1/rooms)에서 이 DTO를 쓰지 않는 이유:
 *   방마다 참가자 명단까지 보내면 응답 크기가 (방 수 × 인원)만큼 커진다.
 *   로비에서는 인원 수(currentPlayers)만 있으면 되므로 명단은 단건 조회에서만 내려준다.
 *
 * JSON 예시 (부모 필드가 먼저, players가 마지막에 붙은 평평한 구조)
 *   {"id":1,"title":"초보만","hostUserId":3,"maxPlayers":8,"currentPlayers":2,"status":"WAITING","gameId":null,"privateRoom":false,
 *    "players":[{"userId":3,"nickname":"유진","ready":false}, ...]}
 */
@Getter
public class RoomDetailResponseDto extends RoomResponseDto {

    private final List<RoomPlayerResponseDto> players; // 입장 순서대로. 첫 번째 사람이 다음 방장 후보

    /** 공통 필드는 super(room)이 채우고, 여기서는 참가자 명단만 변환한다. 외부에서는 from()을 사용한다. */
    private RoomDetailResponseDto(Room room) {
        super(room);
        this.players = room.getPlayers().stream()
                .map(RoomPlayerResponseDto::from)
                .toList();
    }

    /**
     * Room으로 상세 응답을 만든다. (방 단건 조회)
     *
     * 부모의 static from(Room)과 이름·인자가 같지만 static 메서드라 오버라이드가 아니라 "가리기(hiding)"다.
     * 호출하는 쪽에서 클래스 이름을 붙여 부르므로(RoomDetailResponseDto.from) 어느 쪽이 불릴지 헷갈리지 않는다.
     * 반환 타입은 RoomDetailResponseDto라 players까지 포함된다.
     */
    public static RoomDetailResponseDto from(Room room) {
        return new RoomDetailResponseDto(room);
    }
}
