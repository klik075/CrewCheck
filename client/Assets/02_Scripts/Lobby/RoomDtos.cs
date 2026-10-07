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

    /// <summary>서버 에러 코드 중 로비에서 따로 처리하는 것 (ApiResult.errorCode와 비교)</summary>
    public static class RoomErrorCode
    {
        /// <summary>비밀방 입장 시 비밀번호 없음/불일치 (403). 비밀번호 팝업을 닫지 않고 다시 입력받는다.</summary>
        public const string WrongRoomPassword = "WRONG_ROOM_PASSWORD";
    }

    /// <summary>
    /// 비밀방 비밀번호 규칙 (서버 RoomService와 같은 규칙): 숫자(0~9)만, 최소 4자리, 최대 길이 제한 없음.
    /// 방 만들기 팝업과 비밀번호 입력 팝업이 같이 쓴다. 최종 검증은 서버가 다시 한다.
    /// </summary>
    public static class RoomPasswordRule
    {
        public const int MinLength = 4;

        /// <summary>규칙을 설명하는 안내 문구</summary>
        public static readonly string Description = $"숫자 {MinLength}자리 이상";

        /// <summary>규칙에 맞는 비밀번호인지 (null/빈 문자열이면 false)</summary>
        public static bool IsValid(string password)
        {
            if (string.IsNullOrEmpty(password) || password.Length < MinLength) return false;
            foreach (char c in password)
                if (c < '0' || c > '9') return false; // char.IsDigit은 전각 숫자 등도 통과시키므로 직접 비교
            return true;
        }
    }

    /// <summary>
    /// POST /api/v1/rooms 요청 body. 방장 id는 서버가 토큰에서 꺼내므로 보내지 않는다.
    /// 예) 공개방 {"title":"초보만","maxPlayers":8,"privateRoom":false,"password":""}
    ///     비밀방 {"title":"친구만","maxPlayers":8,"privateRoom":true,"password":"0427"}
    /// </summary>
    [Serializable]
    public class CreateRoomRequest
    {
        public string title;
        public int maxPlayers;    // 서버 규칙: 4~12명
        public bool privateRoom;  // 비밀방 여부 (서버 키 이름과 같아야 함)
        public string password;   // 비밀방 비밀번호. 숫자 문자열("0427" - 앞자리 0 유지). 공개방이면 서버가 무시한다
    }

    /// <summary>
    /// POST /api/v1/rooms/{roomId}/players 요청 body (비밀방 입장용).
    /// 공개방은 이 body 없이(null) 보낸다.
    /// </summary>
    [Serializable]
    public class JoinRoomRequest
    {
        public string password;
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
        public bool privateRoom;    // 비밀방이면 true → 목록에 자물쇠 표시, 입장 시 비밀번호 팝업. (비밀번호 자체는 서버가 절대 보내지 않는다)

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
