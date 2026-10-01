using System;
using System.Collections.Generic;
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
    /// GetRoom / GetPlayers / LeaveRoom / StartGame 은 Game 씬(대기 화면)에서 사용하도록 미리 만들어 둔 것이다.
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
            ApiClient.GetList(RoomsPath, onDone);
        }

        /// <summary>
        /// 방 생성. POST /api/v1/rooms
        /// 만든 사람은 방장으로 자동 입장된다. (따로 JoinRoom을 부를 필요 없음)
        /// 실패: 400 제목 없음 / 인원 범위(4~12) 벗어남
        /// </summary>
        public static void CreateRoom(string title, int maxPlayers, Action<ApiResult<RoomResponse>> onDone)
        {
            var body = new CreateRoomRequest { title = title, maxPlayers = maxPlayers };
            ApiClient.Post(RoomsPath, body, onDone);
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
        /// 실패: 409 게임 중 / 정원 초과 / 이미 참가 중, 400 존재하지 않는 방
        /// </summary>
        public static void JoinRoom(long roomId, Action<ApiResult<RoomResponse>> onDone)
        {
            ApiClient.Post($"{RoomsPath}/{roomId}/players", null, onDone);
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
