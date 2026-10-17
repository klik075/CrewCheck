using System;

// POST /api/members/guest 요청 body (응답은 로그인과 같은 AuthResponse)
[Serializable]
public class GuestLoginRequest
{
    public string guestUuid;
}

// POST /api/members/login/google | /login/kakao (소셜 로그인)
// POST /api/members/link/google | /link/kakao (계정 후연동) 공용 요청 body
[Serializable]
public class SocialTokenRequest
{
    public string token; // 소셜 SDK에서 받은 토큰 문자열
}

// POST /api/members/login/google/code | /login/kakao/code, /link/google/code | /link/kakao/code
// 웹(WebGL) 팝업에서 받은 인가 코드 요청 body. redirectUri는 카카오만 쓴다.
[Serializable]
public class SocialCodeRequest
{
    public string code;
    public string redirectUri;
}

// 계정 후연동 성공 응답: { "memberId": 1, "linkedProvider": "GOOGLE", "message": "..." }
[Serializable]
public class SocialLinkResponse
{
    public long memberId;
    public string linkedProvider;
    public string message;
}

public enum SocialProvider
{
    Google,
    Kakao
}
