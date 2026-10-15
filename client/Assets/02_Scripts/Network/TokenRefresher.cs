using System;
using System.Collections.Generic;

namespace WhoisntCitizen.Network
{
    /// <summary>토큰 재발급 시도의 결과</summary>
    public enum RefreshOutcome
    {
        /// <summary>새 액세스/리프레시 토큰을 받아 저장했다.</summary>
        Success,
        /// <summary>서버가 리프레시 토큰을 거절했다(만료·폐기·불일치). 다시 로그인해야 한다.</summary>
        Rejected,
        /// <summary>서버에 닿지 못했거나 서버 오류다. 세션은 그대로 두고 나중에 다시 시도할 수 있다.</summary>
        Failed,
    }

    /// <summary>
    /// 리프레시 토큰으로 액세스 토큰을 다시 받는다. POST /api/auth/refresh (Authorization 헤더 없이 body로 인증)
    ///
    ///   - 동시에 여러 요청이 401을 받아도 서버에는 재발급 요청을 한 번만 보낸다. (리프레시 토큰은 한 번 쓰면 무효라서
    ///     같은 토큰으로 두 번 보내면 두 번째가 거절되고 서버가 토큰을 폐기해 버린다)
    ///   - 성공하면 AuthSession을 새 토큰으로 바꾸고 실시간 연결(WebSocket)도 새 토큰으로 다시 연결한다.
    ///   - 거절당했을 때 세션 정리/타이틀 이동은 부른 쪽이 정한다. (ApiClient가 401 처리를 이어 간다)
    /// </summary>
    public static class TokenRefresher
    {
        private static bool isRefreshing;
        private static readonly List<Action<RefreshOutcome>> waiters = new List<Action<RefreshOutcome>>();

        // 도메인 리로드를 끈 상태로 플레이해도 이전 플레이의 값이 남지 않도록 초기화한다.
        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            isRefreshing = false;
            waiters.Clear();
        }

        /// <summary>재발급을 시도한다. 이미 진행 중이면 그 결과를 같이 받는다. 리프레시 토큰이 없으면 바로 Rejected.</summary>
        public static void Refresh(Action<RefreshOutcome> onDone)
        {
            if (onDone != null) waiters.Add(onDone);
            if (isRefreshing) return;

            string refreshToken = AuthSession.RefreshToken;
            if (string.IsNullOrEmpty(refreshToken))
            {
                Complete(RefreshOutcome.Rejected);
                return;
            }

            isRefreshing = true;
            var body = new RefreshTokenRequest { refreshToken = refreshToken };
            ApiRunner.Run(ApiClient.Send(ApiConfig.BaseUrl, "POST", AuthService.RefreshPath, body,
                needsAuth: false, ApiConfig.TimeoutSeconds, raw =>
                {
                    ApiResult<AuthResponse> result = ApiResult<AuthResponse>.From(raw, JsonHelper.FromJson<AuthResponse>);

                    if (result.success && result.data != null && !string.IsNullOrEmpty(result.data.accessToken)
                        && !string.IsNullOrEmpty(result.data.refreshToken))
                    {
                        AuthSession.UpdateTokens(result.data.accessToken, result.data.refreshToken);
                        RealtimeSession.OnTokenRefreshed();
                        Complete(RefreshOutcome.Success);
                    }
                    else if (result.statusCode == 400 || result.statusCode == 401 || result.statusCode == 403)
                    {
                        UnityEngine.Debug.LogWarning($"[Api] 토큰 재발급 거절({result.statusCode}): {result.message}");
                        Complete(RefreshOutcome.Rejected);
                    }
                    else
                    {
                        UnityEngine.Debug.LogWarning($"[Api] 토큰 재발급 실패({result.statusCode}): {result.message}");
                        Complete(RefreshOutcome.Failed);
                    }
                }));
        }

        private static void Complete(RefreshOutcome outcome)
        {
            isRefreshing = false;
            var callbacks = new List<Action<RefreshOutcome>>(waiters);
            waiters.Clear();
            foreach (Action<RefreshOutcome> callback in callbacks) callback(outcome);
        }
    }
}
