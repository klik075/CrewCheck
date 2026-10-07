using System;
using System.Collections.Generic;
using UnityEngine.Networking;
using WhoisntCitizen.Network;

namespace WhoisntCitizen.Lobby
{
    /// <summary>
    /// 로비/방 관련 서버 API 모음. 모든 요청에 토큰(Authorization: Bearer)이 자동으로 붙는다.
    ///
    /// 사용 예
    ///   RoomApi.GetRooms(result =>
    ///   {
    ///       if (this == null) return;                 // 응답 전에 씬이 바뀐 경우 대비
    ///       if (!result.success) { ShowError(result.message); return; }
    ///       foreach (RoomResponse room in result.data) { ... }
    ///   });
    ///
    /// Lobby 씬은 GetRooms / CreateRoom / JoinRoom / GetRoom 을 사용한다.
    /// GetRoom / LeaveRoom / StartGame 은 Room 씬(대기실, RoomUIController)에서 사용한다.
    /// </summary>
    public static class RoomApi
    {
        private const string RoomsPath = "/api/v1/rooms";

        /// <summary>
        /// 방 목록 조회. GET /api/v1/rooms
        /// 대기 중인 방과 게임 중인 방이 모두 온다. (status로 구분)
        /// </summary>
        public static void GetRooms(Action<ApiResult<List<RoomResponse>>> onDone)
        {
            GetRooms(null, onDone);
        }

        /// <summary>
        /// 방 제목 검색. GET /api/v1/rooms?keyword=초보
        /// keyword가 null/공백이면 전체 목록과 같다.
        /// 서버 규칙: 제목 부분 일치, 대소문자·공백 무시, 비밀방 포함. 공백을 뺀 검색어가 30자를 넘으면 400.
        /// 한글·공백·특수문자가 깨지지 않도록 keyword는 URL 인코딩해서 보낸다.
        /// </summary>
        public static void GetRooms(string keyword, Action<ApiResult<List<RoomResponse>>> onDone)
        {
            string path = string.IsNullOrWhiteSpace(keyword)
                ? RoomsPath
                : RoomsPath + "?keyword=" + UnityWebRequest.EscapeURL(keyword.Trim());
            ApiClient.GetList(path, onDone);
        }

        /// <summary>
        /// 방 생성. POST /api/v1/rooms
        /// 만든 사람은 방장으로 자동 입장된다. (따로 JoinRoom을 부를 필요 없음, 비밀방이어도 방장은 비밀번호 입력 없음)
        /// 실패: 400 제목 없음 / 인원 범위(4~12) 벗어남 / 비밀방인데 비밀번호가 숫자 4자리 이상이 아님
        /// </summary>
        /// <param name="privateRoom">비밀방 여부</param>
        /// <param name="password">비밀방 비밀번호 (숫자 문자열). 공개방이면 null이어도 된다 (서버가 무시)</param>
        public static void CreateRoom(string title, int maxPlayers, bool privateRoom, string password,
            Action<ApiResult<RoomResponse>> onDone)
        {
            var body = new CreateRoomRequest
            {
                title = title,
                maxPlayers = maxPlayers,
                privateRoom = privateRoom,
                password = privateRoom ? password : null, // 공개방이면 보내지 않는다 (JsonUtility는 null을 ""로 보냄 → 서버가 무시)
            };
            ApiClient.Post(RoomsPath, body, onDone);
        }

        /// <summary>공개방 생성 (privateRoom = false)</summary>
        public static void CreateRoom(string title, int maxPlayers, Action<ApiResult<RoomResponse>> onDone)
        {
            CreateRoom(title, maxPlayers, false, null, onDone);
        }

        /// <summary>
        /// 방 상세 조회 (참가자 목록, status, gameId 포함). GET /api/v1/rooms/{roomId}
        /// 대기 화면에서 1초쯤마다 호출해서 status가 IN_GAME이 되면 gameId로 게임 화면에 들어간다.
        /// 실패: 400 존재하지 않는 방
        /// </summary>
        public static void GetRoom(long roomId, Action<ApiResult<RoomDetailResponse>> onDone)
        {
            ApiClient.Get($"{RoomsPath}/{roomId}", onDone);
        }

        /// <summary>
        /// 방 입장. POST /api/v1/rooms/{roomId}/players
        /// 공개방/비밀방 모두 같은 API다. 비밀방이면 password를 body로 보내고, 공개방이면 body 없이 보낸다.
        /// 실패: 403 WRONG_ROOM_PASSWORD 비밀번호 없음/불일치 (errorCode로 확인, 횟수 제한 없음)
        ///       409 게임 중 / 정원 초과 / 이미 참가 중, 400 존재하지 않는 방
        /// </summary>
        /// <param name="password">비밀방 비밀번호. 공개방이면 null</param>
        public static void JoinRoom(long roomId, string password, Action<ApiResult<RoomResponse>> onDone)
        {
            object body = password != null ? new JoinRoomRequest { password = password } : null;
            ApiClient.Post($"{RoomsPath}/{roomId}/players", body, onDone);
        }

        /// <summary>공개방 입장 (비밀번호 없음)</summary>
        public static void JoinRoom(long roomId, Action<ApiResult<RoomResponse>> onDone)
        {
            JoinRoom(roomId, null, onDone);
        }

        /// <summary>참가자 목록 조회. GET /api/v1/rooms/{roomId}/players</summary>
        public static void GetPlayers(long roomId, Action<ApiResult<List<RoomPlayerResponse>>> onDone)
        {
            ApiClient.GetList($"{RoomsPath}/{roomId}/players", onDone);
        }

        /// <summary>
        /// 방 나가기. DELETE /api/v1/rooms/{roomId}/players/me (성공 시 204, 본문 없음)
        /// 마지막 사람이 나가면 방이 삭제된다.
        /// 실패: 409 게임 중에는 나갈 수 없음 / 참가 중이 아님
        /// </summary>
        public static void LeaveRoom(long roomId, Action<ApiResult> onDone)
        {
            ApiClient.Delete($"{RoomsPath}/{roomId}/players/me", onDone);
        }

        /// <summary>
        /// 게임 시작 (방장만). POST /api/v1/rooms/{roomId}/games
        /// 성공하면 방이 IN_GAME이 되고 gameId를 돌려준다.
        /// 실패: 409 방장 아님 / 이미 게임 중 / 4명 미만
        /// </summary>
        public static void StartGame(long roomId, Action<ApiResult<StartGameResponse>> onDone)
        {
            ApiClient.Post($"{RoomsPath}/{roomId}/games", null, onDone);
        }
    }
}
