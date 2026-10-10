using System;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

// 로그인 세션(JWT + 유저 정보) 보관용 static 클래스.
// 메모리에만 저장하므로 앱을 종료하면 사라진다. (씬이 바뀌어도 유지됨)
public static class AuthSession
{
    // 토큰 유효 시간(서버가 넣은 exp - iat, 초)과 로그인한 순간의 단조 시각.
    // 기기 시계가 서버와 달라도 남은 시간을 맞게 계산하려고 서버 시각끼리의 차이와 로그인 뒤 흐른 시간만 쓴다.
    private static double tokenLifetimeSeconds = -1;
    private static long loginTimestamp;

    /// <summary>JWT 액세스 토큰. /api/v1/** 요청의 Authorization 헤더에 사용</summary>
    public static string AccessToken { get; private set; }

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
    public static void SetSession(long memberId, long userId, string username, string nickname, string accessToken)
    {
        MemberId = memberId;
        UserId = userId;
        Username = username;
        Nickname = nickname;
        AccessToken = accessToken;
        tokenLifetimeSeconds = ReadTokenLifetimeSeconds(accessToken);
        loginTimestamp = Stopwatch.GetTimestamp();
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

    /// <summary>로그아웃 / 토큰 만료 시 호출</summary>
    public static void Clear()
    {
        MemberId = 0;
        UserId = 0;
        Username = null;
        Nickname = null;
        AccessToken = null;
        tokenLifetimeSeconds = -1;
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
