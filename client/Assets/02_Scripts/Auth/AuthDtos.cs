using System;

// 회원가입/로그인 공용 요청 DTO (AuthTestScene의 AuthNetworkManager에서 사용)
[Serializable]
public class AuthRequest
{
    public string username;
    public string password;
}

// POST /api/members/login 요청 body
[Serializable]
public class LoginRequest
{
    public string username;
    public string password;
}

// POST /api/members/signup 요청 body (서버 SignupRequest: username, password, nickname 모두 필수)
//   username : 영문 소문자+숫자 4~12자
//   password : 영문+숫자+특수문자(!@#$%^&*) 8~20자
//   nickname : 2~10자, 중복 불가
[Serializable]
public class SignupRequest
{
    public string username;
    public string password;
    public string nickname;
}

// 성공 응답
//   회원가입: { "memberId": 1, "userId": 1, "username": "...", "nickname": "...", "message": "..." }
//   로그인  : { "memberId": 1, "userId": 1, "username": "...", "nickname": "...", "accessToken": "...", "message": "..." }
[Serializable]
public class AuthResponse
{
    public long memberId;      // 로그인 계정(Member) id. JWT의 sub 값
    public long userId;        // 유저 프로필(User) id. 로비/게임에서 플레이어를 구분하는 값 (방장 id, 투표 대상 id 등)
    public string username;    // 로그인 아이디
    public string nickname;    // 게임에서 표시되는 닉네임
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
