using System.Threading.Tasks;

// 소셜 로그인(Google, Kakao)에서 서버에 보낼 값(SocialCredential)을 받아오는 부분의 인터페이스.
// 타이틀 로그인과 로비 계정 연동은 이 인터페이스만 사용하므로, 플랫폼마다 구현체만 바꿔 끼우면 된다.
//   에디터          : MockSocialAuthProvider (더미 토큰)
//   WebGL 빌드      : WebGLSocialAuthProvider (브라우저 팝업 → 인가 코드)
//   Android/iOS     : (TODO) 네이티브 SDK → 토큰
public interface ISocialAuthProvider
{
    /// <summary>구글 로그인 후 서버에 보낼 값을 돌려준다. 실패/취소 시 예외 또는 null/빈 값.</summary>
    Task<SocialCredential> GetGoogleCredentialAsync();

    /// <summary>카카오 로그인 후 서버에 보낼 값을 돌려준다. 실패/취소 시 예외 또는 null/빈 값.</summary>
    Task<SocialCredential> GetKakaoCredentialAsync();
}

// 현재 사용할 소셜 인증 구현체 보관소. 지정하지 않으면 플랫폼에 맞는 기본 구현체를 쓴다.
public static class SocialAuth
{
    private static ISocialAuthProvider provider;

    /// <summary>
    /// 사용할 구현체. 지정하지 않으면 WebGL 빌드는 WebGLSocialAuthProvider, 그 외(에디터 포함)는 Mock을 쓴다.
    /// 모바일 SDK 구현체를 만들면 앱 시작 시 SocialAuth.Provider = new NativeSocialAuthProvider(); 로 교체한다.
    /// </summary>
    public static ISocialAuthProvider Provider
    {
        get => provider ?? (provider = CreateDefault());
        set => provider = value;
    }

    private static ISocialAuthProvider CreateDefault()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        return new WebGLSocialAuthProvider();
#else
        return new MockSocialAuthProvider();
#endif
    }

    /// <summary>제공자별 로그인 값 요청</summary>
    public static Task<SocialCredential> GetCredentialAsync(SocialProvider socialProvider)
    {
        return socialProvider == SocialProvider.Google
            ? Provider.GetGoogleCredentialAsync()
            : Provider.GetKakaoCredentialAsync();
    }

    // 도메인 리로드를 끈 플레이 모드에서도 이전 값이 남지 않게 한다.
    [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => provider = null;
}
