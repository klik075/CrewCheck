using System;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

// 로그인 세션(JWT + 유저 정보) 보관용 static 클래스.
// 액세스 토큰과 유저 정보는 메모리에만 저장하므로 앱을 종료하면 사라진다. (씬이 바뀌어도 유지됨)
// 리프레시 토큰은 PlayerPrefs에도 저장한다. (로그아웃·세션 만료 때 지운다)
public static class AuthSession
{
    // 토큰 유효 시간(서버가 넣은 exp - iat, 초)과 로그인한 순간의 단조 시각.
    // 기기 시계가 서버와 달라도 남은 시간을 맞게 계산하려고 서버 시각끼리의 차이와 로그인 뒤 흐른 시간만 쓴다.
    private static double tokenLifetimeSeconds = -1;
    private static long loginTimestamp;

    private const string RefreshPrefsKey = "auth_refresh_token"; // 같은 PC의 다른 클라이언트와 겹치지 않게 GuestAccountManager와 같은 접미사를 붙인다
    private static string refreshToken;

    /// <summary>JWT 액세스 토큰. /api/v1/** 요청의 Authorization 헤더에 사용</summary>
    public static string AccessToken { get; private set; }

    /// <summary>리프레시 토큰. 액세스 토큰이 만료됐을 때 POST /api/auth/refresh 로 새 토큰을 받는 데 쓴다. 없으면 null</summary>
    public static string RefreshToken
    {
        get
        {
            if (string.IsNullOrEmpty(refreshToken)) refreshToken = LoadRefreshToken();
            return refreshToken;
        }
    }

    /// <summary>로그인 계정(Member) id</summary>
    public static long MemberId { get; private set; }

    /// <summary>유저 프로필(User) id. 로비/게임에서 "나"를 구분하는 값 (예: 방장 여부 = hostUserId == UserId)</summary>
    public static long UserId { get; private set; }

    /// <summary>로그인 아이디</summary>
    public static string Username { get; private set; }

    /// <summary>게임에서 표시되는 닉네임</summary>
    public static string Nickname { get; private set; }

    public static bool IsAuthenticated => !string.IsNullOrEmpty(AccessToken);

    /// <summary>
    /// 토큰이 만료되기까지 남은 분. 로그인하지 않았거나 토큰에서 유효 시간을 읽지 못하면 null.
    /// 게임 중 토큰이 만료되면 상태 조회가 401로 끊기고, 서버는 60초 뒤 연결이 끊긴 것으로 보고 사망 처리한다.
    /// </summary>
    public static double? TokenMinutesLeft
    {
        get
        {
            if (!IsAuthenticated || tokenLifetimeSeconds < 0) return null;
            double elapsed = (Stopwatch.GetTimestamp() - loginTimestamp) / (double)Stopwatch.Frequency;
            return (tokenLifetimeSeconds - elapsed) / 60.0;
        }
    }

    /// <summary>로그인 성공 시 호출 (서버 로그인 응답의 값을 모두 저장)</summary>
    public static void SetSession(long memberId, long userId, string username, string nickname, string accessToken, string refreshToken = null)
    {
        MemberId = memberId;
        UserId = userId;
        Username = username;
        Nickname = nickname;
        AccessToken = accessToken;
        tokenLifetimeSeconds = ReadTokenLifetimeSeconds(accessToken);
        loginTimestamp = Stopwatch.GetTimestamp();
        StoreRefreshToken(refreshToken);

        // 새 토큰으로 실시간 연결(WebSocket)을 시작한다. 모든 로그인(게스트·계정·소셜)이 여기를 지난다.
        WhoisntCitizen.Network.RealtimeSession.OnLoggedIn();
    }

    /// <summary>
    /// (이전 버전 호환용) userId/nickname 없이 저장한다.
    /// AuthTestScene의 AuthNetworkManager가 아직 이 버전을 사용한다.
    /// </summary>
    public static void SetSession(long memberId, string username, string accessToken)
    {
        SetSession(memberId, 0, username, null, accessToken);
    }

    /// <summary>닉네임 변경 성공 시 호출 (세션의 닉네임만 갱신)</summary>
    public static void SetNickname(string nickname)
    {
        Nickname = nickname;
    }

    /// <summary>
    /// 토큰 재발급에 성공했을 때 호출. 액세스/리프레시 토큰과 만료 시각만 바꾸고 유저 정보와 실시간 연결은 건드리지 않는다.
    /// (실시간 연결을 새 토큰으로 다시 맺는 일은 TokenRefresher가 RealtimeSession으로 한다)
    /// </summary>
    public static void UpdateTokens(string accessToken, string newRefreshToken)
    {
        AccessToken = accessToken;
        tokenLifetimeSeconds = ReadTokenLifetimeSeconds(accessToken);
        loginTimestamp = Stopwatch.GetTimestamp();
        StoreRefreshToken(newRefreshToken);
    }

    /// <summary>로그아웃 / 토큰 만료 시 호출. 실시간 연결도 끊고 리프레시 토큰도 지운다.</summary>
    public static void Clear()
    {
        WhoisntCitizen.Network.RealtimeSession.OnLoggedOut();

        MemberId = 0;
        UserId = 0;
        Username = null;
        Nickname = null;
        AccessToken = null;
        tokenLifetimeSeconds = -1;
        StoreRefreshToken(null);
    }

    private static string PrefsKey => RefreshPrefsKey + GuestAccountManager.InstanceSuffix;

    private static string LoadRefreshToken()
    {
        return UnityEngine.PlayerPrefs.GetString(PrefsKey, string.Empty);
    }

    // null/빈 값이면 저장된 토큰을 지운다.
    private static void StoreRefreshToken(string token)
    {
        refreshToken = token;
        if (string.IsNullOrEmpty(token)) UnityEngine.PlayerPrefs.DeleteKey(PrefsKey);
        else UnityEngine.PlayerPrefs.SetString(PrefsKey, token);
        UnityEngine.PlayerPrefs.Save();
    }

    /// <summary>
    /// JWT 본문(두 번째 조각, base64url)의 exp - iat(초). 서명은 검증하지 않는다(남은 시간 안내용).
    /// 읽지 못하면 -1.
    /// </summary>
    private static double ReadTokenLifetimeSeconds(string token)
    {
        if (string.IsNullOrEmpty(token)) return -1;
        string[] parts = token.Split('.');
        if (parts.Length < 2) return -1;
        try
        {
            string base64 = parts[1].Replace('-', '+').Replace('_', '/');
            base64 = base64.PadRight(base64.Length + (4 - base64.Length % 4) % 4, '=');
            string payload = Encoding.UTF8.GetString(Convert.FromBase64String(base64));
            Match exp = Regex.Match(payload, "\"exp\"\\s*:\\s*(\\d+)");
            Match iat = Regex.Match(payload, "\"iat\"\\s*:\\s*(\\d+)");
            if (!exp.Success || !iat.Success) return -1;
            return long.Parse(exp.Groups[1].Value) - long.Parse(iat.Groups[1].Value);
        }
        catch (FormatException)
        {
            return -1;
        }
    }

    /// <summary>인증이 필요한 요청에 사용할 Authorization 헤더 값 ("Bearer xxx")</summary>
    public static string AuthorizationHeader => IsAuthenticated ? $"Bearer {AccessToken}" : null;

    /// <summary>UI/로그 표시용: 토큰 앞부분만 잘라서 반환 (전체 토큰은 노출하지 않음)</summary>
    public static string TokenPreview(int length = 10)
    {
        if (!IsAuthenticated) return string.Empty;
        return AccessToken.Length <= length ? AccessToken : AccessToken.Substring(0, length) + "...";
    }
}
