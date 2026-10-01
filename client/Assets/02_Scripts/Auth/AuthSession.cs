// 로그인 세션(JWT) 보관용 static 클래스. 메모리에만 저장하며 앱 종료 시 사라진다.
public static class AuthSession
{
    public static string AccessToken { get; private set; }
    public static long MemberId { get; private set; }
    public static string Username { get; private set; }

    public static bool IsAuthenticated => !string.IsNullOrEmpty(AccessToken);

    // 로그인 성공 시 호출
    public static void SetSession(long memberId, string username, string accessToken)
    {
        MemberId = memberId;
        Username = username;
        AccessToken = accessToken;
    }

    // 로그아웃 시 호출
    public static void Clear()
    {
        MemberId = 0;
        Username = null;
        AccessToken = null;
    }

    // 인증이 필요한 요청에 사용할 Authorization 헤더 값 ("Bearer xxx")
    public static string AuthorizationHeader => IsAuthenticated ? $"Bearer {AccessToken}" : null;

    // UI/로그 표시용: 토큰 앞부분만 잘라서 반환 (전체 토큰은 노출하지 않음)
    public static string TokenPreview(int length = 10)
    {
        if (!IsAuthenticated) return string.Empty;
        return AccessToken.Length <= length ? AccessToken : AccessToken.Substring(0, length) + "...";
    }
}
