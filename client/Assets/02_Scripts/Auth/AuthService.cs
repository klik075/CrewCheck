using System;
using WhoisntCitizen.Network;

// 로그인/계정 연동 API 호출 모음. 통신은 모두 ApiClient(서버 주소/타임아웃은 ApiConfig)를 거친다.
// 로그인 성공 시 JWT와 유저 정보(userId, nickname 등)를 AuthSession에 저장한다.
// AuthSession은 static이라 씬이 바뀌어도 유지되므로 로비/게임에서 그대로 읽을 수 있다.
public static class AuthService
{
    public const string GuestPath = "/api/members/guest";
    public const string LoginPath = "/api/members/login";
    public const string SignupPath = "/api/members/signup";
    public const string RefreshPath = "/api/auth/refresh";
    public const string LogoutPath = "/api/auth/logout";

    // 토큰(모바일)은 /login/google, 인가 코드(웹)는 /login/google/code 로 보낸다.
    private static string LoginPathOf(SocialProvider p, SocialCredentialKind kind) =>
        (p == SocialProvider.Google ? "/api/members/login/google" : "/api/members/login/kakao") + CodeSuffix(kind);
    private static string LinkPathOf(SocialProvider p, SocialCredentialKind kind) =>
        (p == SocialProvider.Google ? "/api/members/link/google" : "/api/members/link/kakao") + CodeSuffix(kind);
    private static string CodeSuffix(SocialCredentialKind kind) => kind == SocialCredentialKind.Code ? "/code" : "";

    // 토큰/코드에 맞는 요청 body를 만든다.
    private static object SocialBody(SocialCredential credential) =>
        credential.Kind == SocialCredentialKind.Code
            ? (object)new SocialCodeRequest { code = credential.Value, redirectUri = credential.RedirectUri }
            : new SocialTokenRequest { token = credential.Value };

    /// <summary>현재 로그인한 계정이 게스트인지 (게스트 로그인 시 서버가 username을 guest_...로 만든다)</summary>
    public static bool IsGuestAccount => AuthSession.IsAuthenticated
        && !string.IsNullOrEmpty(AuthSession.Username)
        && AuthSession.Username.StartsWith("guest_", StringComparison.OrdinalIgnoreCase);

    /// <summary>게스트 로그인. 저장된 UUID(없으면 새로 발급)로 호출한다.</summary>
    public static void GuestLogin(Action<ApiResult<AuthResponse>> onDone)
    {
        var body = new GuestLoginRequest { guestUuid = GuestAccountManager.GetOrCreateUuid() };
        ApiClient.Post<AuthResponse>(GuestPath, body, r => Finish(r, onDone), requireAuth: false);
    }

    /// <summary>일반 계정(ID/PW) 로그인</summary>
    public static void AccountLogin(string username, string password, Action<ApiResult<AuthResponse>> onDone)
    {
        var body = new LoginRequest { username = username, password = password };
        ApiClient.Post<AuthResponse>(LoginPath, body, r => Finish(r, onDone), requireAuth: false);
    }

    /// <summary>소셜 로그인. credential은 SocialAuth에서 받은 값(모바일: 토큰, 웹: 인가 코드)</summary>
    public static void SocialLogin(SocialProvider provider, SocialCredential credential, Action<ApiResult<AuthResponse>> onDone)
    {
        ApiClient.Post<AuthResponse>(LoginPathOf(provider, credential.Kind), SocialBody(credential), r => Finish(r, onDone), requireAuth: false);
    }

    /// <summary>
    /// 현재 계정(게스트/일반)에 소셜 계정을 연동한다. 현재 JWT가 Authorization 헤더로 자동 첨부된다.
    /// 이미 다른 계정에 연동된 소셜 계정이면 409 (result.IsConflict).
    /// </summary>
    public static void LinkSocial(SocialProvider provider, SocialCredential credential, Action<ApiResult<SocialLinkResponse>> onDone)
    {
        ApiClient.Post<SocialLinkResponse>(LinkPathOf(provider, credential.Kind), SocialBody(credential), onDone, requireAuth: true);
    }

    /// <summary>
    /// 로그아웃: 서버에 리프레시 토큰 폐기를 알리고(응답은 기다리지 않는다) 세션을 비운다.
    /// 세션 만료(401)로 비울 때는 이미 토큰이 무효이므로 이 함수가 아니라 AuthSession.Clear()를 쓴다.
    /// </summary>
    public static void Logout()
    {
        string refreshToken = AuthSession.RefreshToken;
        if (!string.IsNullOrEmpty(refreshToken))
        {
            // 씬이 바뀌어도 요청이 끝까지 나가도록 ApiRunner가 돌린다. 실패해도 서버 쪽 토큰은 7일 뒤 스스로 만료된다.
            ApiClient.Post(LogoutPath, new RefreshTokenRequest { refreshToken = refreshToken }, null, requireAuth: false);
        }
        AuthSession.Clear();
    }

    // 로그인 응답을 세션에 저장한다. accessToken이 없으면 실패로 바꾼다.
    private static void Finish(ApiResult<AuthResponse> result, Action<ApiResult<AuthResponse>> onDone)
    {
        if (result.success)
        {
            AuthResponse res = result.data;
            if (string.IsNullOrEmpty(res.accessToken))
            {
                result.success = false;
                result.message = "로그인 응답이 올바르지 않습니다.";
            }
            else
            {
                AuthSession.SetSession(res.memberId, res.userId, res.username, res.nickname, res.accessToken, res.refreshToken);
            }
        }
        onDone?.Invoke(result);
    }
}
