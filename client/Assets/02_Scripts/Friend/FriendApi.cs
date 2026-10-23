using System;
using System.Collections.Generic;
using WhoisntCitizen.Network;

namespace WhoisntCitizen.Friend
{
    /// <summary>
    /// 친구 서버 API 모음. 모든 요청에 토큰(Authorization: Bearer)이 자동으로 붙는다.
    /// 응답 콜백에서는 항상 if (this == null) return; 으로 파괴된 UI를 건드리지 않게 한다.
    /// </summary>
    public static class FriendApi
    {
        private const string FriendsPath = "/api/friends";

        /// <summary>내 친구 목록. GET /api/friends</summary>
        public static void GetFriends(Action<ApiResult<List<FriendResponse>>> onDone)
        {
            ApiClient.GetList(FriendsPath, onDone);
        }

        /// <summary>받은 대기 중 친구 요청 목록. GET /api/friends/requests</summary>
        public static void GetRequests(Action<ApiResult<List<FriendRequestResponse>>> onDone)
        {
            ApiClient.GetList($"{FriendsPath}/requests", onDone);
        }

        /// <summary>친구 요청 전송. POST /api/friends/request  body {"targetUserId": 5}</summary>
        public static void SendRequest(long targetUserId, Action<ApiResult> onDone)
        {
            ApiClient.Post($"{FriendsPath}/request", new SendFriendRequest { targetUserId = targetUserId }, onDone);
        }

        /// <summary>친구 요청 수락. POST /api/friends/{friendshipId}/accept</summary>
        public static void Accept(long friendshipId, Action<ApiResult> onDone)
        {
            ApiClient.Post($"{FriendsPath}/{friendshipId}/accept", null, onDone);
        }

        /// <summary>친구 요청 거절 또는 기존 친구 삭제. DELETE /api/friends/{friendshipId}</summary>
        public static void Remove(long friendshipId, Action<ApiResult> onDone)
        {
            ApiClient.Delete($"{FriendsPath}/{friendshipId}", onDone);
        }
    }
}
