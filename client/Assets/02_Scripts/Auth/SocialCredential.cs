// 소셜 로그인 결과로 서버에 보낼 값.
// 모바일 네이티브 SDK는 토큰(Token)을, 웹(WebGL) 팝업은 인가 코드(Code)를 돌려준다.
// AuthService가 Kind를 보고 호출할 서버 API를 고른다. (/login/{provider} 또는 /login/{provider}/code)
public enum SocialCredentialKind
{
    /// <summary>구글 ID 토큰 / 카카오 access token (모바일 SDK)</summary>
    Token,
    /// <summary>구글/카카오 인가 코드 (웹 팝업). 서버가 토큰으로 교환한다.</summary>
    Code
}

public class SocialCredential
{
    public SocialCredentialKind Kind;
    public string Value;

    /// <summary>카카오 인가 코드를 받을 때 쓴 Redirect URI. 서버가 토큰 교환 때 같은 값을 써야 한다. (그 외에는 null)</summary>
    public string RedirectUri;

    public bool IsEmpty => string.IsNullOrEmpty(Value);

    public static SocialCredential FromToken(string token) =>
        new SocialCredential { Kind = SocialCredentialKind.Token, Value = token };

    public static SocialCredential FromCode(string code, string redirectUri = null) =>
        new SocialCredential { Kind = SocialCredentialKind.Code, Value = code, RedirectUri = redirectUri };
}
