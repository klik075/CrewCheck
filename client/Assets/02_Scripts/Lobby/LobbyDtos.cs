using System;
using System.Collections.Generic;

namespace WhoisntCitizen.LobbyTest
{
    // POST /api/members/signup  (서버 SignupRequest: username, password, nickname 모두 필수)
    [Serializable]
    public class SignupRequest
    {
        public string username;
        public string password;
        public string nickname;
    }

    // POST /api/members/login
    // 응답은 기존 AuthResponse { memberId, username, accessToken, message } 를 그대로 재사용한다.
    [Serializable]
    public class LoginRequest
    {
        public string username;
        public string password;
    }

    // POST /api/v1/rooms  (방장 id는 서버가 토큰에서 꺼내므로 body에 넣지 않는다)
    [Serializable]
    public class RoomCreateRequest
    {
        public string title;
        public int maxPlayers;
    }

    // 서버 RoomResponseDto: { id, title, hostUserId, maxPlayers, currentPlayers }
    [Serializable]
    public class RoomInfoResponse
    {
        public long id;
        public string title;
        public long hostUserId;
        public int maxPlayers;
        public int currentPlayers;
    }

    // GET /api/v1/rooms 는 최상위가 배열([ ... ])이라 JsonUtility가 바로 읽지 못한다.
    // 컨트롤러에서 {"items":[ ... ]} 로 감싸서 파싱한다.
    [Serializable]
    public class RoomListResponse
    {
        public List<RoomInfoResponse> items = new List<RoomInfoResponse>();
    }

    // 서버 RoomPlayerResponseDto: { userId, nickname, ready }
    [Serializable]
    public class RoomPlayerResponse
    {
        public long userId;
        public string nickname;
        public bool ready;
    }

    // GET /api/v1/rooms/{id}/players 도 최상위가 배열이라 {"items":[ ... ]} 로 감싸서 파싱한다.
    [Serializable]
    public class RoomPlayerListResponse
    {
        public List<RoomPlayerResponse> items = new List<RoomPlayerResponse>();
    }

    // 네트워크 호출 결과 (성공/실패 공통)
    public class ApiResult
    {
        public bool success;
        public long responseCode;
        public string body;    // 서버가 준 원문
        public string message; // 사람이 읽을 요약
    }
}
