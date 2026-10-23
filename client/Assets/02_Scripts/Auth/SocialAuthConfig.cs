using WhoisntCitizen.Network;

// 웹(WebGL) 소셜 로그인 설정. 값은 서버 담당자/콘솔에서 확인해 채운다.
// 구글 클라이언트 ID와 카카오 REST API 키는 브라우저 주소창에도 드러나는 공개 값이라 클라이언트에 둬도 된다.
// (구글 클라이언트 시크릿, 카카오 클라이언트 시크릿은 절대 여기에 넣지 말고 서버 환경변수에만 둔다)
public static class SocialAuthConfig
{
    /// <summary>
    /// 구글 "웹 애플리케이션" OAuth 클라이언트 ID. 서버의 oauth.google.client-id(GOOGLE_CLIENT_ID)와 같은 값이어야 한다.
    /// Google Cloud Console의 "승인된 JavaScript 원본"에 게임을 서비스하는 도메인(로컬 테스트는 http://localhost:8000)을 등록한다.
    /// </summary>
    public const string GoogleWebClientId = "58755757454-3jo8nh6sflk5kqdk9frv9rp6ii7eldja.apps.googleusercontent.com";

    /// <summary>
    /// 카카오 REST API 키. 서버의 oauth.kakao.rest-api-key(KAKAO_REST_API_KEY)와 같은 앱의 키여야 한다.
    /// </summary>
    public const string KakaoRestApiKey = "d793a308476a5952e00f1ddd0a2d3820";

    /// <summary>
    /// 카카오 로그인 후 돌아올 페이지의 서버 경로. 게임 서버(ApiConfig.BaseUrl)가 직접 서비스하는 고정 주소다.
    /// (itch.io는 게임 주소가 업로드마다 바뀔 수 있어서, 게임 쪽이 아니라 서버 주소를 쓴다)
    /// 예) https://3-38-222-51.sslip.io/kakao-callback.html 를 카카오 콘솔 Redirect URI와
    ///     서버 oauth.kakao.allowed-redirect-uris(KAKAO_ALLOWED_REDIRECT_URIS)에 똑같이 등록한다.
    /// </summary>
    public const string KakaoCallbackPath = "/kakao-callback.html";

    public static string KakaoRedirectUri => ApiConfig.BaseUrl.TrimEnd('/') + KakaoCallbackPath;

    public static bool IsGoogleConfigured => !GoogleWebClientId.StartsWith("CHANGE_ME");
    public static bool IsKakaoConfigured => !KakaoRestApiKey.StartsWith("CHANGE_ME");
}
