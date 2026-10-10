using System;

namespace WhoisntCitizen.Profile
{
    // 유저 프로필 API(/api/users/**) 요청/응답 DTO.
    // JsonUtility는 필드명을 JSON 키와 똑같이 맞춰야 하므로 소문자 camelCase를 유지한다.

    /// <summary>
    /// 프로필 응답. GET /api/users/me, GET /api/users/{userId}, GET /api/users/search
    /// </summary>
    [Serializable]
    public class UserProfileResponse
    {
        public long userId;
        public long memberId;   // 타인 프로필에서는 0일 수 있다
        public string nickname;
        public int level;
        public long gold;
        public int playCount;
        public int winCount;
        public int lossCount;
        public double winRate;  // 서버 계산값. 0~1 / 0~100 중 어느 쪽인지 모호하므로 화면에는 WinRatePercent를 쓴다.

        /// <summary>승률(%). 서버 winRate의 단위에 의존하지 않도록 승/판수로 직접 계산한다. (0~100)</summary>
        public double WinRatePercent => playCount <= 0 ? 0d : (double)winCount / playCount * 100d;
    }

    /// <summary>닉네임 변경 요청. PATCH /api/users/me/nickname</summary>
    [Serializable]
    public class ChangeNicknameRequest
    {
        public string nickname;
    }
}
