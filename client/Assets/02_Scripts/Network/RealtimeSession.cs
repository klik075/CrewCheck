using UnityEngine;

namespace WhoisntCitizen.Network
{
    /// <summary>
    /// 로그인 세션과 실시간 연결(RealtimeClient)의 수명을 맞춘다. AuthSession이 부른다.
    ///
    ///   로그인 성공 (AuthSession.SetSession)  → OnLoggedIn  → Connect (다른 토큰으로 다시 로그인했으면 끊고 새 토큰으로)
    ///   로그아웃·401 만료 (AuthSession.Clear) → OnLoggedOut → Disconnect
    ///   실시간 연결이 토큰 만료로 멈춤(4401) → ApiClient.ExpireSession() → 401과 같은 처리
    ///                                          (안내 "다시 로그인해 주세요" + 세션 비우기 + 타이틀로)
    ///
    /// 화면들은 연결을 신경 쓰지 않고 EnterRoom/LeaveRoom만 부른다.
    /// </summary>
    public static class RealtimeSession
    {
        /// <summary>로그인에 성공해 새 토큰을 받았다.</summary>
        public static void OnLoggedIn()
        {
            if (!Application.isPlaying)
            {
                return; // EditMode 테스트 등에서 세션만 채울 때는 연결하지 않는다
            }

            RealtimeClient realtime = RealtimeClient.Instance;
            realtime.AuthExpired -= OnRealtimeAuthExpired; // 같은 클라이언트에 두 번 걸리지 않게
            realtime.AuthExpired += OnRealtimeAuthExpired;

            if (realtime.WantsConnection)
            {
                // 이미 연결(또는 연결 시도) 중인데 다시 로그인했다 → 옛 토큰의 연결을 끊고 새 토큰으로 다시 연결한다.
                realtime.Disconnect();
            }
            realtime.Connect();
        }

        /// <summary>로그아웃했거나 세션이 만료됐다.</summary>
        public static void OnLoggedOut()
        {
            // 플레이 종료 중에 새로 만들지 않도록 이미 있는 클라이언트만 쓴다.
            RealtimeClient realtime = RealtimeClient.ExistingInstance;
            if (realtime != null)
            {
                realtime.Disconnect();
            }
        }

        /// <summary>
        /// 서버가 토큰 만료로 실시간 연결을 닫았다(4401). REST도 곧 401이 나므로 기다리지 않고 같은 만료 처리를 한다.
        /// </summary>
        private static void OnRealtimeAuthExpired()
        {
            ApiClient.ExpireSession();
        }
    }
}
