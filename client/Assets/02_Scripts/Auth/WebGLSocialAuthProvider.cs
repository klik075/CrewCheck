#if UNITY_WEBGL && !UNITY_EDITOR
using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using UnityEngine;

// WebGL 빌드용 소셜 로그인. 브라우저 팝업(SocialAuthWeb.jslib)으로 구글/카카오 로그인을 하고
// 받은 "인가 코드"를 SocialCredential(Kind=Code)로 돌려준다. 코드 → 토큰 교환은 서버(/login/{provider}/code)가 한다.
//
// 흐름: 버튼 클릭 → Get*CredentialAsync → jslib가 팝업을 연다 → 결과(JSON)를 SendMessage로
//       숨은 수신 오브젝트(Receiver)의 OnWebAuthResult에 보낸다 → TaskCompletionSource를 완료한다.
// 반드시 사용자의 버튼 클릭 직후에 불러야 한다. (시작하자마자 부르면 브라우저가 팝업을 차단한다)
public class WebGLSocialAuthProvider : ISocialAuthProvider
{
    [DllImport("__Internal")] private static extern void SocialAuthWeb_RequestGoogleCode(string clientId, string receiverName);
    [DllImport("__Internal")] private static extern void SocialAuthWeb_RequestKakaoCode(string restApiKey, string redirectUri, string receiverName);

    private TaskCompletionSource<SocialCredential> pending;

    public Task<SocialCredential> GetGoogleCredentialAsync()
    {
        if (!SocialAuthConfig.IsGoogleConfigured)
            return Task.FromException<SocialCredential>(new Exception("구글 웹 클라이언트 ID가 설정되지 않았습니다. (SocialAuthConfig.GoogleWebClientId)"));

        return Begin(receiverName => SocialAuthWeb_RequestGoogleCode(SocialAuthConfig.GoogleWebClientId, receiverName), isKakao: false);
    }

    public Task<SocialCredential> GetKakaoCredentialAsync()
    {
        if (!SocialAuthConfig.IsKakaoConfigured)
            return Task.FromException<SocialCredential>(new Exception("카카오 REST API 키가 설정되지 않았습니다. (SocialAuthConfig.KakaoRestApiKey)"));

        return Begin(receiverName => SocialAuthWeb_RequestKakaoCode(SocialAuthConfig.KakaoRestApiKey, SocialAuthConfig.KakaoRedirectUri, receiverName), isKakao: true);
    }

    private Task<SocialCredential> Begin(Action<string> startBrowserLogin, bool isKakao)
    {
        // 이전 로그인이 아직 대기 중이면 취소로 끝낸다. (jslib도 새 로그인을 시작하면서 이전 것을 정리한다)
        pending?.TrySetException(new Exception("이전 로그인 요청이 취소되었습니다."));

        var tcs = new TaskCompletionSource<SocialCredential>();
        pending = tcs;
        WebAuthReceiver.GetOrCreate().Listen(json => OnResult(tcs, json, isKakao));
        try
        {
            startBrowserLogin(WebAuthReceiver.ObjectName);
        }
        catch (Exception e)
        {
            tcs.TrySetException(e);
        }
        return tcs.Task;
    }

    private void OnResult(TaskCompletionSource<SocialCredential> tcs, string json, bool isKakao)
    {
        WebAuthResult result = null;
        try { result = JsonUtility.FromJson<WebAuthResult>(json); }
        catch (Exception) { /* 아래에서 실패 처리 */ }

        if (result != null && result.ok && !string.IsNullOrEmpty(result.code))
        {
            tcs.TrySetResult(SocialCredential.FromCode(result.code, isKakao ? result.redirectUri : null));
        }
        else
        {
            tcs.TrySetException(new Exception(result != null && !string.IsNullOrEmpty(result.error) ? result.error : "소셜 로그인에 실패했습니다."));
        }
        if (pending == tcs) pending = null;
    }

    [Serializable]
    private class WebAuthResult
    {
        public bool ok;
        public string code;
        public string redirectUri;
        public string error;
    }

    // jslib의 SendMessage를 받는 숨은 오브젝트. 씬이 바뀌어도 유지된다.
    private class WebAuthReceiver : MonoBehaviour
    {
        public const string ObjectName = "SocialAuthWebReceiver";
        private static WebAuthReceiver instance;
        private Action<string> handler;

        public static WebAuthReceiver GetOrCreate()
        {
            if (instance == null)
            {
                var go = new GameObject(ObjectName);
                DontDestroyOnLoad(go);
                instance = go.AddComponent<WebAuthReceiver>();
            }
            return instance;
        }

        public void Listen(Action<string> onResult) => handler = onResult;

        // SocialAuthWeb.jslib가 SendMessage(ObjectName, "OnWebAuthResult", json)로 부른다.
        public void OnWebAuthResult(string json)
        {
            var h = handler;
            handler = null;
            h?.Invoke(json);
        }
    }
}
#endif
