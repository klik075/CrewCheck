using System;
using System.Collections.Generic;
using UnityEngine.Networking;
using WhoisntCitizen.Network;

namespace WhoisntCitizen.Profile
{
    /// <summary>
    /// 유저 프로필 서버 API 모음. 모든 요청에 토큰(Authorization: Bearer)이 자동으로 붙는다.
    /// (RoomApi와 같은 방식: ApiClient를 거치므로 401이면 타이틀로 돌아간다)
    ///
    /// 사용 예
    ///   UserApi.GetMe(result =>
    ///   {
    ///       if (this == null) return;   // 응답 전에 씬이 바뀐 경우 대비
    ///       if (!result.success) { ShowError(result.message); return; }
    ///       Show(result.data);
    ///   });
    /// </summary>
    public static class UserApi
    {
        private const string UsersPath = "/api/users";

        /// <summary>내 프로필 조회. GET /api/users/me</summary>
        public static void GetMe(Action<ApiResult<UserProfileResponse>> onDone)
        {
            ApiClient.Get($"{UsersPath}/me", onDone);
        }

        /// <summary>타인 프로필 단건 조회. GET /api/users/{userId}</summary>
        public static void GetUser(long userId, Action<ApiResult<UserProfileResponse>> onDone)
        {
            ApiClient.Get($"{UsersPath}/{userId}", onDone);
        }

        /// <summary>
        /// 닉네임으로 유저 검색. GET /api/users/search?nickname=...
        /// 응답이 객체 하나든 배열이든 항상 List로 돌려준다. (서버 응답 형태 변경에 대비)
        /// 한글이 깨지지 않도록 nickname은 URL 인코딩해서 보낸다.
        /// </summary>
        public static void SearchByNickname(string nickname, Action<ApiResult<List<UserProfileResponse>>> onDone)
        {
            string path = $"{UsersPath}/search?nickname={UnityWebRequest.EscapeURL((nickname ?? string.Empty).Trim())}";
            ApiClient.GetRaw(path, raw => onDone?.Invoke(ApiResult<List<UserProfileResponse>>.From(raw, ParseSearchBody)));
        }

        /// <summary>
        /// 닉네임 변경. PATCH /api/users/me/nickname  body {"nickname": "..."}
        /// 응답 본문은 쓰지 않는다. 성공하면 호출한 쪽에서 GetMe로 다시 불러온다.
        /// </summary>
        public static void ChangeNickname(string nickname, Action<ApiResult> onDone)
        {
            ApiClient.Patch($"{UsersPath}/me/nickname", new ChangeNicknameRequest { nickname = nickname }, onDone);
        }

        // 검색 응답 본문이 "[...]"면 배열로, "{...}"면 한 명짜리 리스트로 파싱한다.
        private static List<UserProfileResponse> ParseSearchBody(string body)
        {
            if (string.IsNullOrWhiteSpace(body)) return null;

            if (body.TrimStart().StartsWith("["))
                return JsonHelper.FromJsonArray<UserProfileResponse>(body);

            UserProfileResponse single = JsonHelper.FromJson<UserProfileResponse>(body);
            return single == null ? null : new List<UserProfileResponse> { single };
        }
    }
}
