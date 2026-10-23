#if UNITY_WEBGL && !UNITY_EDITOR
using System;
using UnityEngine;

namespace WhoisntCitizen.Network
{
    /// <summary>
    /// WebGL 로컬 테스트용: 게임 주소에 ?api=http://localhost:8080 을 붙여 열면 그 서버로 접속한다.
    /// 예) http://localhost:8000/?api=http://localhost:8080
    ///
    /// 보안: 이 값으로 로그인 정보가 전송되므로, 누군가 만든 링크로 다른 서버에 접속하게 되면 안 된다.
    /// 그래서 localhost / 127.0.0.1 주소만 받아들이고, 그 밖의 값은 무시한다. (배포 서버 주소는 그대로 유지)
    /// </summary>
    public static class ApiBaseUrlOverride
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Apply()
        {
            string value = ReadQueryParameter(Application.absoluteURL, "api");
            if (string.IsNullOrEmpty(value)) return;

            if (!Uri.TryCreate(value, UriKind.Absolute, out Uri uri)) return;
            bool isHttp = uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps;
            bool isLocal = uri.Host == "localhost" || uri.Host == "127.0.0.1";
            if (!isHttp || !isLocal)
            {
                Debug.LogWarning("[ApiBaseUrlOverride] localhost 주소만 허용합니다. 무시합니다.");
                return;
            }

            ApiConfig.BaseUrl = uri.GetLeftPart(UriPartial.Authority);
            Debug.Log("[ApiBaseUrlOverride] 서버 주소를 로컬로 바꿨습니다: " + ApiConfig.BaseUrl);
        }

        private static string ReadQueryParameter(string url, string name)
        {
            int q = string.IsNullOrEmpty(url) ? -1 : url.IndexOf('?');
            if (q < 0) return null;
            string query = url.Substring(q + 1);
            int hash = query.IndexOf('#');
            if (hash >= 0) query = query.Substring(0, hash);

            foreach (string pair in query.Split('&'))
            {
                int eq = pair.IndexOf('=');
                if (eq <= 0) continue;
                if (pair.Substring(0, eq) == name) return Uri.UnescapeDataString(pair.Substring(eq + 1));
            }
            return null;
        }
    }
}
#endif
