using System.Threading.Tasks;

// 소셜 SDK(Google Sign-In, Kakao)에서 로그인 토큰을 받아오는 부분의 인터페이스.
// 타이틀 로그인과 로비 계정 연동은 이 인터페이스만 사용하므로,
// 실기기 빌드에서는 구현체만 바꿔 끼우면 된다. (SocialAuth.Provider)
public interface ISocialAuthProvider
{
    /// <summary>구글 로그인 후 토큰을 돌려준다. 실패/취소 시 예외 또는 null/빈 문자열.</summary>
    Task<string> GetGoogleTokenAsync();

    /// <summary>카카오 로그인 후 토큰을 돌려준다. 실패/취소 시 예외 또는 null/빈 문자열.</summary>
    Task<string> GetKakaoTokenAsync();
}

// 현재 사용할 소셜 인증 구현체 보관소. 지정하지 않으면 Mock을 쓴다.
public static class SocialAuth
{
    private static ISocialAuthProvider provider;

    /// <summary>
    /// 사용할 구현체. 실기기 빌드에서는 앱 시작 시 실제 SDK 구현체를 넣는다.
    /// 예) SocialAuth.Provider = new NativeSocialAuthProvider();
    /// </summary>
    public static ISocialAuthProvider Provider
    {
        get => provider ?? (provider = new MockSocialAuthProvider());
        set => provider = value;
    }

    /// <summary>제공자별 토큰 요청</summary>
    public static Task<string> GetTokenAsync(SocialProvider socialProvider)
    {
        return socialProvider == SocialProvider.Google
            ? Provider.GetGoogleTokenAsync()
            : Provider.GetKakaoTokenAsync();
    }

    // 도메인 리로드를 끈 플레이 모드에서도 이전 값이 남지 않게 한다.
    [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => provider = null;
}
