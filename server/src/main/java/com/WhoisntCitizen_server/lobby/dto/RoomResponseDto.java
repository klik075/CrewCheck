package com.WhoisntCitizen_server.lobby.dto;

import com.WhoisntCitizen_server.lobby.domain.room.Room;
import com.WhoisntCitizen_server.lobby.domain.room.RoomStatus;
import lombok.Getter;

/**
 * 방 요약 응답. 로비 화면에서 방 한 줄을 그리는 데 필요한 정보만 담는다. (참가자 명단 없음)
 *
 * 사용하는 API
 *   - GET  /api/v1/rooms                  방 목록 (List로 응답)
 *   - POST /api/v1/rooms                  방 생성
 *   - POST /api/v1/rooms/{roomId}/players 방 입장
 *
 * 상속 구조
 *   RoomResponseDto (방 공통 정보)
 *     └ RoomDetailResponseDto (+ 참가자 명단 players) ─ GET /api/v1/rooms/{roomId}
 *
 *   두 응답이 같은 필드를 따로 선언하던 중복을 없애기 위해 상세 응답이 이 클래스를 상속한다.
 *   방에 공통으로 보여줄 필드를 추가할 때는 이 클래스에만 추가하면 두 응답 모두에 들어간다.
 *   (필드 선언 + 아래 protected 생성자에서 값 채우기, 두 군데만 수정)
 *
 * JSON 모양
 *   상속을 써도 JSON은 중첩되지 않고 평평하게 나간다. Jackson은 부모 필드를 먼저, 자식 필드를 뒤에 쓴다.
 *   예) {"id":1,"title":"초보만","hostUserId":3,"maxPlayers":8,"currentPlayers":2,"status":"WAITING","gameId":null}
 *   Unity(JsonUtility)는 필드 이름으로 값을 채우므로, 필드 이름을 바꾸면 Unity RoomDtos.cs도 같이 바꿔야 한다.
 *
 * 주의
 *   - 응답 전용 DTO라 모든 필드는 final이고 setter가 없다. Room에서만 만들 수 있다. (from 사용)
 *   - 방 비밀번호 같은 민감한 값은 절대 여기에 넣지 않는다. 목록 API로 모든 유저에게 그대로 노출된다.
 */
@Getter
public class RoomResponseDto {

    private final Long id;
    private final String title;
    private final Long hostUserId;   // 방장의 userId (User.id). 방장이 나가면 다음 사람으로 바뀐다
    private final int maxPlayers;
    private final int currentPlayers; // 현재 참가 인원 = room.players.size()
    private final RoomStatus status;  // WAITING / IN_GAME (Unity 로비에서 "게임 중" 표시, 입장 버튼 비활성화)
    private final String gameId;      // 진행 중인 게임 id (대기 중이면 null). 방장이 아닌 참가자는 방을 조회해 이 값으로 게임 화면에 들어간다

    /**
     * Room → 응답 필드 매핑. 공통 필드의 매핑은 이 생성자 한 곳에서만 한다.
     *
     * protected인 이유: 외부에서는 from()으로만 만들게 하고,
     * 자식 클래스(RoomDetailResponseDto)는 super(room)으로 공통 필드를 채운 뒤 자기 필드만 추가로 채운다.
     * (예전 @AllArgsConstructor 방식은 상속 시 부모 필드까지 자식 생성자 인자로 다시 나열해야 해서 사용하지 않는다)
     */
    protected RoomResponseDto(Room room) {
        this.id = room.getId();
        this.title = room.getTitle();
        this.hostUserId = room.getHostUserId();
        this.maxPlayers = room.getMaxPlayers();
        this.currentPlayers = room.getPlayers().size();
        this.status = room.getStatus();
        this.gameId = room.getGameId();
    }

    /** Room으로 요약 응답을 만든다. (방 목록 / 생성 / 입장) */
    public static RoomResponseDto from(Room room) {
        return new RoomResponseDto(room);
    }
}
