// 로그인 세션(JWT + 유저 정보) 보관용 static 클래스.
// 메모리에만 저장하므로 앱을 종료하면 사라진다. (씬이 바뀌어도 유지됨)
public static class AuthSession
{
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

    /// <summary>로그인 성공 시 호출 (서버 로그인 응답의 값을 모두 저장)</summary>
    public static void SetSession(long memberId, long userId, string username, string nickname, string accessToken)
    {
        MemberId = memberId;
        UserId = userId;
        Username = username;
        Nickname = nickname;
        AccessToken = accessToken;
    }

    /// <summary>
    /// (이전 버전 호환용) userId/nickname 없이 저장한다.
    /// AuthTestScene의 AuthNetworkManager가 아직 이 버전을 사용한다.
    /// </summary>
    public static void SetSession(long memberId, string username, string accessToken)
    {
        SetSession(memberId, 0, username, null, accessToken);
    }

    /// <summary>로그아웃 / 토큰 만료 시 호출</summary>
    public static void Clear()
    {
        MemberId = 0;
        UserId = 0;
        Username = null;
        Nickname = null;
        AccessToken = null;
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
