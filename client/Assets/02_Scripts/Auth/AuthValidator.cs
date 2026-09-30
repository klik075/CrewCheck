using System.Text.RegularExpressions;

// 클라이언트 1차 유효성 검증 (서버 검증을 대체하지 않음)
public static class AuthValidator
{
    private static readonly Regex UsernameRegex = new Regex(@"^[a-z0-9]{4,12}$", RegexOptions.Compiled);
    private static readonly Regex PasswordRegex = new Regex(
        @"^(?=.*[A-Za-z])(?=.*\d)(?=.*[!@#$%^&*])[A-Za-z\d!@#$%^&*]{8,20}$", RegexOptions.Compiled);

    public static bool IsValidUsername(string username)
    {
        return !string.IsNullOrEmpty(username) && UsernameRegex.IsMatch(username);
    }

    public static bool IsValidPassword(string password)
    {
        return !string.IsNullOrEmpty(password) && PasswordRegex.IsMatch(password);
    }

    // 통과하면 true, 실패하면 false + 경고 메시지
    public static bool Validate(string username, string password, out string error)
    {
        if (!IsValidUsername(username))
        {
            error = "아이디는 영문 소문자와 숫자 조합 4~12자리여야 합니다.";
            return false;
        }

        if (!IsValidPassword(password))
        {
            error = "비밀번호는 영문, 숫자, 특수문자(!@#$%^&*)를 모두 포함한 8~20자리여야 합니다.";
            return false;
        }

        error = null;
        return true;
    }
}
