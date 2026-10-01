using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using WhoisntCitizen.Common;
using WhoisntCitizen.Lobby;

namespace WhoisntCitizen.Network
{
    /// <summary>
    /// 서버 REST 호출 공용 클라이언트. 모든 API 호출은 이 클래스를 거친다.
    /// (Title 작업의 ApiClient.Send와 Lobby 작업의 Get/Post/GetList/Delete를 하나로 합친 버전)
    ///
    /// ─ 사용법 1: 콜백 방식 (권장) ─────────────────────────────────────────
    ///   ApiClient.Get&lt;RoomDetailResponse&gt;("/api/v1/rooms/1", result => { ... });
    ///   ApiClient.Post&lt;AuthResponse&gt;("/api/members/login", body, result => { ... }, requireAuth: false);
    ///   → 서버 주소/타임아웃은 ApiConfig 값을 쓰고, 코루틴은 ApiRunner가 대신 돌린다.
    ///
    /// ─ 사용법 2: 코루틴 방식 (기존 Title 코드 호환) ──────────────────────────
    ///   StartCoroutine(ApiClient.Send(baseUrl, "POST", path, body, needsAuth, timeout, onDone));
    ///   → 호출한 쪽이 코루틴을 직접 돌리고 yield return으로 끝날 때까지 기다릴 수 있다.
    ///
    /// 공통 처리
    ///   - requireAuth(needsAuth)가 true면 Authorization: Bearer {accessToken} 헤더를 붙인다.
    ///     (/api/v1/** 는 모두 토큰 필요, 회원가입/로그인만 불필요)
    ///   - 서버 에러 { "code": "...", "message": "..." } 를 파싱해서 result.message에 넣는다.
    ///   - 인증이 필요한 요청에서 401(토큰 없음/만료)을 받으면 세션을 비우고 타이틀 씬으로 보낸다.
    ///
    /// 주의 (콜백에서 꼭 확인할 것)
    ///   ApiRunner는 씬이 바뀌어도 살아 있어서, 응답 전에 씬이 바뀌면 콜백이 실행될 때
    ///   요청한 UI 오브젝트는 이미 파괴된 상태다. MonoBehaviour 콜백 첫 줄에
    ///   if (this == null) return; 을 넣어서 파괴된 오브젝트를 건드리지 않게 한다.
    /// </summary>
    public static class ApiClient
    {
        /// <summary>기본 서버 주소. (기존 코드 호환용, 실제 값은 ApiConfig에서 관리)</summary>
        public const string DefaultBaseUrl = ApiConfig.DefaultBaseUrl;

        /// <summary>401을 받아 세션이 만료되었을 때 호출된다. (필요한 곳에서 구독해서 사용)</summary>
        public static event Action Unauthorized;

        // 도메인 리로드를 끈 상태로 플레이해도 이벤트 구독이 남지 않도록 초기화한다.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Unauthorized = null;
        }

        // ==================================================================
        // 사용법 1: 콜백 방식 - 응답 본문을 T로 파싱해서 돌려준다
        // ==================================================================

        /// <summary>GET 요청. 응답 본문(JSON 객체)을 T로 파싱한다.</summary>
        public static void Get<T>(string path, Action<ApiResult<T>> onDone, bool requireAuth = true) where T : class
        {
            Run(UnityWebRequest.kHttpVerbGET, path, null, requireAuth,
                raw => onDone?.Invoke(ApiResult<T>.From(raw, JsonHelper.FromJson<T>)));
        }

        /// <summary>GET 요청. 응답 본문이 배열("[ ... ]")일 때 List&lt;T&gt;로 파싱한다.</summary>
        public static void GetList<T>(string path, Action<ApiResult<List<T>>> onDone, bool requireAuth = true)
        {
            Run(UnityWebRequest.kHttpVerbGET, path, null, requireAuth,
                raw => onDone?.Invoke(ApiResult<List<T>>.From(raw, JsonHelper.FromJsonArray<T>)));
        }

        /// <summary>POST 요청. body는 JSON으로 보내고(null이면 본문 없음), 응답 본문을 T로 파싱한다.</summary>
        public static void Post<T>(string path, object body, Action<ApiResult<T>> onDone, bool requireAuth = true) where T : class
        {
            Run(UnityWebRequest.kHttpVerbPOST, path, body, requireAuth,
                raw => onDone?.Invoke(ApiResult<T>.From(raw, JsonHelper.FromJson<T>)));
        }

        // ==================================================================
        // 사용법 1: 콜백 방식 - 응답 본문이 없거나 필요 없는 요청
        // ==================================================================

        /// <summary>POST 요청. 응답 본문은 파싱하지 않는다. (회원가입처럼 성공 여부만 필요할 때)</summary>
        public static void Post(string path, object body, Action<ApiResult> onDone, bool requireAuth = true)
        {
            Run(UnityWebRequest.kHttpVerbPOST, path, body, requireAuth, onDone);
        }

        /// <summary>DELETE 요청. (예: 방 나가기 → 204 No Content)</summary>
        public static void Delete(string path, Action<ApiResult> onDone, bool requireAuth = true)
        {
            Run(UnityWebRequest.kHttpVerbDELETE, path, null, requireAuth, onDone);
        }

        // ==================================================================
        // 사용법 2: 코루틴 방식 (기존 Title 작업 코드와 같은 시그니처)
        // ==================================================================

        /// <summary>
        /// 요청 하나를 보내는 코루틴. 호출한 쪽에서 StartCoroutine으로 실행한다.
        /// </summary>
        /// <param name="baseUrl">서버 주소 (보통 ApiConfig.BaseUrl)</param>
        /// <param name="method">"GET" / "POST" / "DELETE" 등</param>
        /// <param name="path">"/api/v1/rooms" 처럼 / 로 시작하는 경로</param>
        /// <param name="body">JSON으로 보낼 객체. 본문이 없으면 null</param>
        /// <param name="needsAuth">true면 Authorization 헤더를 붙이고 401 시 로그아웃 처리</param>
        /// <param name="timeoutSeconds">요청 제한 시간(초)</param>
        /// <param name="onDone">결과 콜백 (성공/실패 모두 호출)</param>
        public static IEnumerator Send(string baseUrl, string method, string path, object body,
            bool needsAuth, int timeoutSeconds, Action<ApiResult> onDone)
        {
            // 토큰이 필요한 요청인데 로그인 정보가 없으면 서버에 보내지 않고 바로 401로 처리한다.
            if (needsAuth && !AuthSession.IsAuthenticated)
            {
                HandleUnauthorized();
                onDone?.Invoke(new ApiResult { success = false, statusCode = 401, message = "로그인이 필요합니다." });
                yield break;
            }

            string jsonBody = JsonHelper.ToJson(body);

            using (var req = new UnityWebRequest(baseUrl + path, method))
            {
                req.downloadHandler = new DownloadHandlerBuffer();
                if (jsonBody != null)
                    req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(jsonBody));

                req.SetRequestHeader("Content-Type", "application/json");
                if (needsAuth) req.SetRequestHeader("Authorization", AuthSession.AuthorizationHeader);
                req.timeout = timeoutSeconds;

                if (ApiConfig.LogRequests)
                    Debug.Log($"[Api] → {method} {path}" + (jsonBody != null ? $"  {MaskPassword(jsonBody)}" : ""));

                yield return req.SendWebRequest();

                ApiResult result = BuildResult(req);

                if (ApiConfig.LogRequests)
                {
                    string log = $"[Api] ← {method} {path}  {result.statusCode}  {(result.success ? "성공" : result.message)}";
                    if (result.success) Debug.Log(log + (string.IsNullOrEmpty(result.body) ? "" : $"\n{MaskToken(result.body)}"));
                    else Debug.LogWarning(log);
                }

                // 인증이 필요한 요청에서 401 → 토큰 만료/무효. 세션을 비우고 타이틀로 보낸다.
                // (로그인/회원가입은 needsAuth=false라 비밀번호가 틀려도 여기에 걸리지 않는다)
                if (needsAuth && result.IsUnauthorized) HandleUnauthorized();

                onDone?.Invoke(result);
            }
        }

        // ==================================================================
        // 내부 처리
        // ==================================================================

        // 콜백 방식 요청을 ApiConfig 설정으로 실행한다. (코루틴은 ApiRunner가 돌린다)
        private static void Run(string method, string path, object body, bool requireAuth, Action<ApiResult> onDone)
        {
            ApiRunner.Run(Send(ApiConfig.BaseUrl, method, path, body, requireAuth, ApiConfig.TimeoutSeconds, onDone));
        }

        /// <summary>UnityWebRequest 결과를 ApiResult로 변환한다.</summary>
        private static ApiResult BuildResult(UnityWebRequest req)
        {
            var result = new ApiResult
            {
                statusCode = req.responseCode,
                body = req.downloadHandler != null ? req.downloadHandler.text : null,
            };

            switch (req.result)
            {
                case UnityWebRequest.Result.Success: // 2xx
                    result.success = true;
                    result.message = $"HTTP {req.responseCode}";
                    break;

                case UnityWebRequest.Result.ProtocolError: // 4xx, 5xx
                    ApiErrorResponse err = JsonHelper.FromJson<ApiErrorResponse>(result.body);
                    result.errorCode = err?.code;
                    result.message = ExtractServerMessage(req.responseCode, err);
                    break;

                default: // ConnectionError(서버 꺼짐/타임아웃), DataProcessingError
                    result.statusCode = 0;
                    result.message = "서버에 연결할 수 없습니다. 서버가 켜져 있는지 확인해 주세요.";
                    break;
            }

            return result;
        }

        /// <summary>서버 에러 응답을 사용자에게 보여줄 문장으로 정리한다.</summary>
        private static string ExtractServerMessage(long statusCode, ApiErrorResponse err)
        {
            string message = !string.IsNullOrEmpty(err?.message) ? err.message : err?.error;

            // @Valid 검증 실패는 Spring 예외 문장 전체("Validation failed for argument ...")가 오므로 그대로 보여주지 않는다.
            if (!string.IsNullOrEmpty(message) && message.StartsWith("Validation failed"))
                return "입력값이 올바르지 않습니다.";

            if (!string.IsNullOrEmpty(message)) return message;
            return DefaultMessage(statusCode);
        }

        /// <summary>서버가 에러 메시지를 주지 않았을 때 쓰는 상태 코드별 기본 문구</summary>
        private static string DefaultMessage(long statusCode)
        {
            switch (statusCode)
            {
                case 400: return "잘못된 요청입니다.";
                case 401: return "인증이 필요하거나 만료되었습니다. 다시 로그인해 주세요."; // 401은 본문이 비어서 옴
                case 403: return "권한이 없습니다.";
                case 404: return "요청한 정보를 찾을 수 없습니다.";
                case 409: return "요청을 처리할 수 없는 상태입니다.";
                default: return statusCode >= 500 ? "서버 내부 오류가 발생했습니다."
                                                  : $"요청에 실패했습니다. (HTTP {statusCode})";
            }
        }

        /// <summary>
        /// 세션 만료 처리: 로그인/방 정보를 비우고 타이틀 씬으로 보낸다.
        /// 이미 타이틀 씬이면 씬 이동은 하지 않는다.
        /// </summary>
        private static void HandleUnauthorized()
        {
            Debug.LogWarning("[Api] 인증이 없거나 만료되어 로그아웃합니다.");
            AuthSession.Clear();
            RoomSession.Clear();
            Unauthorized?.Invoke();

            if (!SceneLoader.IsCurrent(SceneType.Title)) SceneLoader.Load(SceneType.Title);
        }

        // ------------------------------------------------------------------
        // 로그 마스킹: Console에 비밀번호/토큰 원문이 찍히지 않게 가린다.
        // ------------------------------------------------------------------

        // 요청 body의 "password":"..." 값을 ***로 바꾼다.
        private static string MaskPassword(string json)
        {
            return System.Text.RegularExpressions.Regex.Replace(json, "(\"password\"\\s*:\\s*\")[^\"]*(\")", "$1***$2");
        }

        // 응답 body의 "accessToken":"..." 값을 앞 10자만 남긴다.
        private static string MaskToken(string json)
        {
            return System.Text.RegularExpressions.Regex.Replace(json, "(\"accessToken\"\\s*:\\s*\")([^\"]{0,10})[^\"]*(\")", "$1$2...$3");
        }
    }
}
