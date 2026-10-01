using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using WhoisntCitizen.LobbyTest;

namespace WhoisntCitizen.Network
{
    // 서버 REST 호출 공용 헬퍼. 씬/UI에 의존하지 않고 결과를 ApiResult로 돌려준다.
    // 서버 에러 형식: { "code": "...", "message": "..." } (GlobalExceptionHandler)
    public static class ApiClient
    {
        public const string DefaultBaseUrl = "http://localhost:8080";

        public static IEnumerator Send(string baseUrl, string method, string path, object body,
            bool needsAuth, int timeoutSeconds, Action<ApiResult> onDone)
        {
            if (needsAuth && !AuthSession.IsAuthenticated)
            {
                onDone?.Invoke(new ApiResult { success = false, responseCode = 401, message = "로그인이 필요합니다." });
                yield break;
            }

            using (var req = new UnityWebRequest(baseUrl + path, method))
            {
                req.downloadHandler = new DownloadHandlerBuffer();
                if (body != null)
                    req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(JsonUtility.ToJson(body)));
                req.SetRequestHeader("Content-Type", "application/json");
                if (needsAuth) req.SetRequestHeader("Authorization", AuthSession.AuthorizationHeader);
                req.timeout = timeoutSeconds;

                yield return req.SendWebRequest();

                onDone?.Invoke(BuildResult(req));
            }
        }

        private static ApiResult BuildResult(UnityWebRequest req)
        {
            var result = new ApiResult
            {
                responseCode = req.responseCode,
                body = req.downloadHandler != null ? req.downloadHandler.text : null,
            };

            switch (req.result)
            {
                case UnityWebRequest.Result.Success:
                    result.success = true;
                    result.message = $"HTTP {req.responseCode}";
                    break;

                case UnityWebRequest.Result.ProtocolError: // 4xx, 5xx
                    result.message = ExtractServerMessage(req.responseCode, result.body, req.error);
                    break;

                default: // ConnectionError, DataProcessingError, 타임아웃
                    result.message = "서버에 연결할 수 없습니다. 서버가 켜져 있는지 확인해 주세요.";
                    break;
            }
            return result;
        }

        // 서버가 준 message를 사용자에게 보여줄 문장으로 정리한다.
        private static string ExtractServerMessage(long code, string body, string fallback)
        {
            string message = null;
            if (!string.IsNullOrEmpty(body))
            {
                try { message = JsonUtility.FromJson<ErrorResponse>(body)?.message; }
                catch (Exception) { /* HTML 등 JSON이 아닌 응답 */ }
            }

            // @Valid 검증 실패는 Spring 예외 문장 전체가 오므로 그대로 보여주지 않는다.
            if (!string.IsNullOrEmpty(message) && message.StartsWith("Validation failed"))
                return "입력값이 올바르지 않습니다.";

            if (!string.IsNullOrEmpty(message)) return message;
            if (code == 401) return "인증이 필요하거나 만료되었습니다. 다시 로그인해 주세요.";
            if (code >= 500) return "서버 내부 오류가 발생했습니다.";
            return fallback;
        }
    }
}
