using System;

// 회원가입/로그인 공용 요청 DTO
[Serializable]
public class AuthRequest
{
    public string username;
    public string password;
}

// 성공 응답: { "memberId": 1, "username": "...", "message": "..." }
[Serializable]
public class AuthResponse
{
    public long memberId;
    public string username;
    public string accessToken; // 로그인 응답에만 포함 (회원가입 응답에는 없음)
    public string message;
}

// 서버 에러 응답 (커스텀 { "message": "..." } 또는 Spring 기본 { "status", "error", "message" } 모두 대응)
[Serializable]
public class ErrorResponse
{
    public string message;
    public string error;
    public int status;
}

// 네트워크 호출 결과
public class AuthResult
{
    public bool success;
    public long responseCode;
    public string message;
    public AuthResponse data;
}
