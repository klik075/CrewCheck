using System;

namespace WhoisntCitizen.Friend
{
    // 친구 API(/api/friends/**) 요청/응답 DTO.

    /// <summary>내 친구 한 명. GET /api/friends 응답 배열의 원소</summary>
    [Serializable]
    public class FriendResponse
    {
        public long friendshipId;   // 친구 삭제(DELETE /api/friends/{friendshipId})에 사용
        public long friendUserId;
        public string nickname;
        public int level;
        public int playCount;
        public int winCount;
        public double winRate;      // 서버 계산값 (단위 모호) → 화면에는 WinRatePercent를 쓴다.

        /// <summary>승률(%). 승/판수로 직접 계산한다. (0~100)</summary>
        public double WinRatePercent => playCount <= 0 ? 0d : (double)winCount / playCount * 100d;
    }

    /// <summary>받은 친구 요청 한 건. GET /api/friends/requests 응답 배열의 원소</summary>
    [Serializable]
    public class FriendRequestResponse
    {
        public long friendshipId;   // 수락(POST .../accept) / 거절(DELETE)에 사용
        public long requesterUserId;
        public string requesterNickname;
        public string requestedAt;  // ISO 날짜 문자열 그대로 보관
    }

    /// <summary>친구 요청 전송. POST /api/friends/request</summary>
    [Serializable]
    public class SendFriendRequest
    {
        public long targetUserId;
    }
}
