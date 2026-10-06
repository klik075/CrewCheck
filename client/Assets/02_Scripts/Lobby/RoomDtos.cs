using System;
using System.Collections.Generic;

namespace WhoisntCitizen.Lobby
{
    // ======================================================================
    // 서버 로비 API의 요청/응답 DTO.
    // 필드 이름은 서버 JSON 키와 똑같아야 JsonUtility가 값을 채운다. (대소문자 포함)
    // enum(status, phase)은 JsonUtility가 문자열로 읽지 못하므로 string으로 받는다.
    // ======================================================================

    /// <summary>방 상태 값 (서버 RoomStatus enum의 문자열)</summary>
    public static class RoomStatus
    {
        public const string Waiting = "WAITING"; // 대기 중 → 입장 가능
        public const string InGame = "IN_GAME";  // 게임 중 → 입장/나가기 불가 (409)
    }

    /// <summary>POST /api/v1/rooms 요청 body. 방장 id는 서버가 토큰에서 꺼내므로 보내지 않는다.</summary>
    [Serializable]
    public class CreateRoomRequest
    {
        public string title;
        public int maxPlayers; // 서버 규칙: 4~12명
    }

    /// <summary>
    /// 방 요약 정보 (서버 RoomResponseDto). 로비에서 방 한 줄을 그리는 데 필요한 값만 있다. (참가자 명단 없음)
    /// 사용처: GET /api/v1/rooms(목록의 각 항목), POST /api/v1/rooms(생성), POST /api/v1/rooms/{id}/players(입장)
    ///
    /// 상속 구조 (서버와 같은 구조)
    ///   RoomResponse (방 공통 정보)
    ///     └ RoomDetailResponse (+ 참가자 명단 players) ─ GET /api/v1/rooms/{roomId}
    ///
    /// 방 공통 필드를 추가할 때는 이 클래스에만 추가하면 RoomDetailResponse에도 자동으로 생긴다.
    /// JsonUtility는 [Serializable] 부모 클래스의 public 필드도 키 이름으로 채워 준다.
    /// </summary>
    [Serializable]
    public class RoomResponse
    {
        public long id;
        public string title;
        public long hostUserId;     // 방장의 userId (방장이 나가면 서버가 다음 사람으로 바꾼다)
        public int maxPlayers;
        public int currentPlayers;
        public string status;       // "WAITING" / "IN_GAME"
        public string gameId;       // 게임 중일 때만 값이 있음 (대기 중이면 null/빈 문자열)

        public bool IsInGame => status == RoomStatus.InGame;
        public bool IsFull => currentPlayers >= maxPlayers;

        /// <summary>지금 입장 버튼을 눌러도 되는 방인지 (대기 중이고 자리가 남아 있음)</summary>
        public bool CanJoin => !IsInGame && !IsFull;
    }

    /// <summary>방 참가자 (서버 RoomPlayerResponseDto)</summary>
    [Serializable]
    public class RoomPlayerResponse
    {
        public long userId;
        public string nickname;
        public bool ready; // 준비 기능은 아직 서버에 없음 (항상 false)
    }

    /// <summary>
    /// 방 상세 (서버 RoomDetailResponseDto). GET /api/v1/rooms/{roomId}
    /// Room 씬(대기실)에서 주기적으로 조회해서, status가 IN_GAME이 되면 gameId로 게임을 시작한다.
    ///
    /// 공통 필드(id, title, hostUserId, maxPlayers, currentPlayers, status, gameId)와
    /// IsInGame / IsFull / CanJoin은 RoomResponse에서 물려받고, 여기서는 참가자 명단만 추가한다.
    /// 서버 JSON도 상속 전과 같은 평평한 구조라서 키 이름만 맞으면 그대로 읽힌다.
    /// </summary>
    [Serializable]
    public class RoomDetailResponse : RoomResponse
    {
        public List<RoomPlayerResponse> players = new List<RoomPlayerResponse>(); // 입장 순서대로

        /// <summary>해당 userId가 이 방의 참가자인지 확인한다.</summary>
        public bool HasPlayer(long userId)
        {
            if (players == null) return false;
            foreach (RoomPlayerResponse p in players)
                if (p.userId == userId) return true;
            return false;
        }
    }

    /// <summary>게임 시작 응답 (서버 StartGameResponse). POST /api/v1/rooms/{roomId}/games</summary>
    [Serializable]
    public class StartGameResponse
    {
        public string gameId;
        public string phase;       // 시작 직후에는 "NIGHT"
        public int day;
        public string phaseEndsAt; // 현재 페이즈가 끝나는 시각 (ISO-8601 문자열, 예: 2026-10-01T12:00:30Z)
    }
}
