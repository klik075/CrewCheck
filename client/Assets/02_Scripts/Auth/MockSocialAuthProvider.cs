using System;
using System.Threading.Tasks;
using UnityEngine;

// 에디터 테스트용 Mock. 실제 SDK 없이 미리 정해 둔 더미 토큰을 돌려준다.
// [Serializable]이라 TitleController 인스펙터에서 토큰/지연 시간을 바꿀 수 있다.
//
// 주의: 서버가 소셜 토큰을 실제로 구글/카카오에 검증하면 이 더미 토큰은 거절된다.
//       서버 테스트용 설정(검증 우회)이 있거나, 여기에 직접 발급받은 테스트 토큰을 넣어야 로그인이 성공한다.
//
// TODO(실기기 빌드): 이 클래스를 대신할 NativeSocialAuthProvider : ISocialAuthProvider 를 만든다.
//   - GetGoogleTokenAsync : Google Sign-In SDK로 로그인 → ID 토큰(서버 oauth.google.client-id와 같은 클라이언트 ID로 발급)을 반환
//   - GetKakaoTokenAsync  : Kakao SDK로 로그인 → access token을 반환
//   - 앱 시작 시 SocialAuth.Provider = new NativeSocialAuthProvider(); 로 교체 (필요하면 #if UNITY_ANDROID || UNITY_IOS 로 분기)
//   - SDK 콜백은 TaskCompletionSource<string>으로 감싸 Task로 돌려주면 호출 쪽 코드는 바꿀 필요가 없다.
[Serializable]
public class MockSocialAuthProvider : ISocialAuthProvider
{
    public string googleToken = "mock_google_token_123";
    public string kakaoToken = "mock_kakao_token_123";

    [Tooltip("실제 로그인 창이 뜨는 시간을 흉내 내는 지연(밀리초). 0이면 즉시 반환")]
    public int delayMilliseconds = 300;

    public async Task<string> GetGoogleTokenAsync()
    {
        await SimulateLoginWindow();
        Debug.LogWarning("[Auth] Mock 구글 토큰을 사용합니다. (실제 SDK 미연동)");
        return googleToken;
    }

    public async Task<string> GetKakaoTokenAsync()
    {
        await SimulateLoginWindow();
        Debug.LogWarning("[Auth] Mock 카카오 토큰을 사용합니다. (실제 SDK 미연동)");
        return kakaoToken;
    }

    private Task SimulateLoginWindow() => delayMilliseconds > 0 ? Task.Delay(delayMilliseconds) : Task.CompletedTask;
}
