using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using WhoisntCitizen.Network; // MiniJson (AuthSession은 Auth 폴더의 전역 클래스)

namespace WhoisntCitizen.Chat
{
    [Serializable]
    public class ChatMessage
    {
        public string id;         // messageId
        public string type;       // USER(일반) / SYSTEM(입장·퇴장 알림, 공지) / DEAD(사망자 채팅: 서버가 사망자에게만 보내 줌)
        public string userId;     // 시스템 메시지는 "0" (서버 ChatMessage.SYSTEM_USER_ID)
        public string nickname;   // 시스템 메시지는 "SYSTEM"
        public string content;    // message
        public string createdAt;  // 전송 응답에만 있음 (조회 응답에는 없음)
        public bool nightChat;    // 밤에 해적이 입력한 채팅 (type=USER, 서버가 해적에게만 보내 줌 → 내용을 주황색으로 표시)

        public bool IsSystem
        {
            get { return string.Equals(type, "SYSTEM", StringComparison.OrdinalIgnoreCase); }
        }

        /// 게임 중 사망자가 보낸 메시지. 서버가 같은 게임의 사망자에게만 내려 주므로 받은 것은 그대로 (회색으로) 표시하면 됩니다.
        public bool IsDead
        {
            get { return string.Equals(type, "DEAD", StringComparison.OrdinalIgnoreCase); }
        }
    }

    /// <summary>로그인 응답 (MemberDto.AuthResult)</summary>
    public class LoginResult
    {
        public string accessToken;
        public long memberId = -1;
        public long userId = -1;
        public string nickname;
    }

    /// <summary>
    /// 채팅 API 클라이언트 (WhoisntCitizen_server, API v1)
    /// 모든 /api/v1/** 요청에 AuthSession.AuthorizationHeader(Bearer 토큰)를 붙입니다. (로비 LobbyTestController와 같은 세션 사용)
    /// 보낸 사람은 서버가 토큰으로 판단하므로 본문에 userId를 넣지 않습니다.
    ///  - 조회: GET  /api/v1/rooms/{roomId}/messages[?limit=N][&amp;afterId=ID]   200 / 404 방 없음
    ///  - 전송: POST /api/v1/rooms/{roomId}/messages  {"message":"..."}
    ///          201 / 403 방 참가자가 아님 / 404 방 없음 / 400 프로필 없음
    ///  - 공지: POST /api/v1/rooms/{roomId}/system-messages  {"message":"..."}  (헤더 X-Admin-Key, 서버 chat.admin-key가 비어 있으면 생략 가능)
    ///          201 / 403 공지 권한 없음 / 404 방 없음
    ///  - [개발용] 로그인:  POST /api/members/login  {"username","password"}
    ///  - [개발용] 방 참가: POST /api/v1/rooms/{roomId}/players  (로비 API, 입장 시 서버가 시스템 메시지를 남김)
    ///  - [개발용] 방 생성: POST /api/v1/rooms  {"title","maxPlayers"}  (방장이 자동으로 참가)
    /// 오류 콜백의 두 번째 값은 HTTP 상태 코드입니다. (서버에 연결조차 못 했으면 0)
    /// </summary>
    public class ChatApiClient : MonoBehaviour
    {
        [Header("Server")]
        [Tooltip("비워 두면 ApiConfig.BaseUrl(로그인·로비와 같은 서버)을 씁니다. 다른 서버로 채팅만 테스트할 때만 입력하세요.")]
        public string baseUrlOverride = "";
        public string apiPrefix = "/api/v1";
        [Tooltip("채팅할 로비 방 id (0 이하면 [개발용] 설정에 따라 새 방을 만듭니다)")]
        public long roomId = 1;
        [Tooltip("요청 타임아웃(초)")]
        public int timeoutSeconds = 5;
        [Tooltip("시스템 메시지(공지) 전송용 키. 서버 chat.admin-key(환경변수 CHAT_ADMIN_KEY)와 같은 값. 서버 값이 비어 있으면 비워 둬도 됩니다.")]
        public string adminKey = "";

        [Header("JSON 필드명 (API 명세)")]
        public string nightChatField = "nightChat";
        public string messageIdField = "messageId";
        public string typeField = "type";
        public string userIdField = "userId";
        public string nicknameField = "nickname";
        public string messageField = "message";
        public string createdAtField = "createdAt";

        string Base
        {
            get { return (string.IsNullOrEmpty(baseUrlOverride) ? ApiConfig.BaseUrl : baseUrlOverride).TrimEnd('/'); }
        }

        string RoomUrl
        {
            get { return Base + apiPrefix + "/rooms/" + roomId.ToString(CultureInfo.InvariantCulture); }
        }

        string MessagesUrl
        {
            get { return RoomUrl + "/messages"; }
        }

        // ---------------- 채팅 ----------------

        /// <param name="afterId">0 이상이면 이 messageId 이후의 새 메시지만 조회 (폴링용), 음수면 최신 메시지 조회</param>
        /// <param name="limit">가져올 최대 개수 (0 이하면 서버 기본값 50, 최대 200)</param>
        public IEnumerator FetchMessages(long afterId, int limit, Action<List<ChatMessage>> onSuccess, Action<string, long> onError)
        {
            string url = MessagesUrl;
            var query = new List<string>();
            if (limit > 0) query.Add("limit=" + limit.ToString(CultureInfo.InvariantCulture));
            if (afterId >= 0) query.Add("afterId=" + afterId.ToString(CultureInfo.InvariantCulture));
            if (query.Count > 0) url += "?" + string.Join("&", query.ToArray());

            using (var req = UnityWebRequest.Get(url))
            {
                Prepare(req, true);
                yield return req.SendWebRequest();

                if (req.result != UnityWebRequest.Result.Success)
                {
                    if (onError != null) onError(ErrorText("조회 실패", req), req.responseCode);
                    yield break;
                }

                List<ChatMessage> list;
                try { list = ParseMessages(Body(req)); }
                catch (Exception e)
                {
                    if (onError != null) onError("응답 파싱 실패: " + e.Message, req.responseCode);
                    yield break;
                }
                if (onSuccess != null) onSuccess(list);
            }
        }

        /// <summary>채팅 전송: {"message":"..."} (보낸 사람은 토큰으로 식별). 성공 시 서버가 판단한 내 userId를 넘겨줍니다.</summary>
        public IEnumerator PostMessage(string message, Action<long> onSuccess, Action<string, long> onError)
        {
            string body = "{" + MiniJson.Quote(messageField) + ":" + MiniJson.Quote(message) + "}";
            using (var req = PostJson(MessagesUrl, body, true))
            {
                yield return req.SendWebRequest();
                if (req.result != UnityWebRequest.Result.Success)
                {
                    if (onError != null) onError(ErrorText("전송 실패", req), req.responseCode);
                    yield break;
                }
                long senderId = -1;
                try
                {
                    var obj = MiniJson.Parse(Body(req)) as Dictionary<string, object>;
                    if (obj != null) senderId = Long(obj, userIdField);
                }
                catch (Exception) { /* 응답을 못 읽어도 전송은 성공 */ }
                if (onSuccess != null) onSuccess(senderId);
            }
        }

        /// <summary>시스템 메시지(공지) 전송: {"message":"..."}. 채팅창에는 type=SYSTEM(녹색)으로 표시됩니다.</summary>
        public IEnumerator PostSystemMessage(string message, Action onSuccess, Action<string, long> onError)
        {
            string body = "{" + MiniJson.Quote(messageField) + ":" + MiniJson.Quote(message) + "}";
            using (var req = PostJson(RoomUrl + "/system-messages", body, true))
            {
                if (!string.IsNullOrEmpty(adminKey)) req.SetRequestHeader("X-Admin-Key", adminKey);
                yield return req.SendWebRequest();
                if (req.result != UnityWebRequest.Result.Success)
                {
                    if (onError != null) onError(ErrorText("공지 전송 실패", req), req.responseCode);
                    yield break;
                }
                if (onSuccess != null) onSuccess();
            }
        }

        // ---------------- [개발용] 로그인 / 방 ----------------

        /// <summary>[개발용] 로그인. 성공하면 결과를 넘겨주며, AuthSession 저장은 호출하는 쪽에서 합니다. (userId·nickname도 함께 받기 위해 직접 호출)</summary>
        public IEnumerator Login(string username, string password, Action<LoginResult> onSuccess, Action<string, long> onError)
        {
            string body = "{\"username\":" + MiniJson.Quote(username) + ",\"password\":" + MiniJson.Quote(password) + "}";
            using (var req = PostJson(Base + "/api/members/login", body, false))
            {
                yield return req.SendWebRequest();
                if (req.result != UnityWebRequest.Result.Success)
                {
                    if (onError != null) onError(ErrorText("로그인 실패", req), req.responseCode);
                    yield break;
                }

                var obj = MiniJson.Parse(Body(req)) as Dictionary<string, object>;
                var result = new LoginResult();
                if (obj != null)
                {
                    result.accessToken = Str(obj, "accessToken");
                    result.memberId = Long(obj, "memberId");
                    result.userId = Long(obj, "userId");
                    result.nickname = Str(obj, "nickname");
                }
                if (string.IsNullOrEmpty(result.accessToken))
                {
                    if (onError != null) onError("로그인 실패: 응답에 accessToken이 없습니다.", req.responseCode);
                    yield break;
                }
                if (onSuccess != null) onSuccess(result);
            }
        }

        /// <summary>[개발용] 로비 방 참가 (본문 없음, 토큰으로 식별). 이미 참가 중이면 409.</summary>
        public IEnumerator JoinRoom(Action onSuccess, Action<string, long> onError)
        {
            using (var req = PostJson(RoomUrl + "/players", "{}", true))
            {
                yield return req.SendWebRequest();
                if (req.result != UnityWebRequest.Result.Success)
                {
                    if (onError != null) onError(ErrorText("방 참가 실패", req), req.responseCode);
                    yield break;
                }
                if (onSuccess != null) onSuccess();
            }
        }

        /// <summary>[개발용] 로비 방 생성. 성공하면 새 방 id를 넘겨줍니다. (방장은 자동으로 참가)</summary>
        public IEnumerator CreateRoom(string title, int maxPlayers, Action<long> onSuccess, Action<string, long> onError)
        {
            string body = "{\"title\":" + MiniJson.Quote(title) + ",\"maxPlayers\":" + maxPlayers.ToString(CultureInfo.InvariantCulture) + "}";
            using (var req = PostJson(Base + apiPrefix + "/rooms", body, true))
            {
                yield return req.SendWebRequest();
                if (req.result != UnityWebRequest.Result.Success)
                {
                    if (onError != null) onError(ErrorText("방 생성 실패", req), req.responseCode);
                    yield break;
                }
                var obj = MiniJson.Parse(Body(req)) as Dictionary<string, object>;
                long id = obj != null ? Long(obj, "id") : -1;
                if (id < 0)
                {
                    if (onError != null) onError("방 생성 실패: 응답에 id가 없습니다.", req.responseCode);
                    yield break;
                }
                if (onSuccess != null) onSuccess(id);
            }
        }

        // ---------------- 공통 ----------------

        void Prepare(UnityWebRequest req, bool auth)
        {
            req.timeout = timeoutSeconds;
            req.SetRequestHeader("Accept", "application/json; charset=utf-8");
            if (auth && AuthSession.IsAuthenticated)
                req.SetRequestHeader("Authorization", AuthSession.AuthorizationHeader);
        }

        UnityWebRequest PostJson(string url, string json, bool auth)
        {
            var req = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST);
            req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json; charset=utf-8");
            Prepare(req, auth);
            return req;
        }

        /// <summary>한글이 깨지지 않도록 응답 바이트를 항상 UTF-8로 디코딩</summary>
        static string Body(UnityWebRequest req)
        {
            return req.downloadHandler == null || req.downloadHandler.data == null
                ? ""
                : Encoding.UTF8.GetString(req.downloadHandler.data);
        }

        /// <summary>
        /// 오류 문구. 서버가 {"code":"FORBIDDEN","message":"현재 채팅할 수 없는 플레이어입니다."}처럼
        /// 이유를 보내면 그 message를 보여주고, 없으면 HTTP 오류를 그대로 보여줍니다.
        /// </summary>
        static string ErrorText(string prefix, UnityWebRequest req)
        {
            if (req.responseCode == 401)
                return prefix + " (401): 로그인이 필요합니다. (토큰 없음 또는 만료)";
            if (req.responseCode > 0)
            {
                try
                {
                    var obj = MiniJson.Parse(Body(req)) as Dictionary<string, object>;
                    object msg;
                    if (obj != null && obj.TryGetValue("message", out msg) && msg != null && msg.ToString().Length > 0)
                        return prefix + " (" + req.responseCode + "): " + msg;
                }
                catch (Exception) { /* 본문이 JSON이 아니면 아래 기본 문구 사용 */ }
            }
            return prefix + ": " + req.error;
        }

        /// <summary>응답: [{"messageId":100,"userId":1,"nickname":"철수","message":"...","type":"USER"}]</summary>
        List<ChatMessage> ParseMessages(string json)
        {
            var result = new List<ChatMessage>();
            var arr = MiniJson.Parse(json) as List<object>;
            if (arr == null) return result;

            foreach (var item in arr)
            {
                var d = item as Dictionary<string, object>;
                if (d == null) continue;
                result.Add(new ChatMessage
                {
                    id = Str(d, messageIdField),
                    type = Str(d, typeField),
                    userId = Str(d, userIdField),
                    nickname = Str(d, nicknameField),
                    content = Str(d, messageField),
                    createdAt = Str(d, createdAtField),
                    nightChat = Bool(d, nightChatField)
                });
            }
            return result;
        }

        static string Str(Dictionary<string, object> d, string key)
        {
            object v;
            if (string.IsNullOrEmpty(key) || !d.TryGetValue(key, out v) || v == null) return "";
            if (v is double) return ((double)v).ToString(CultureInfo.InvariantCulture);
            return v.ToString();
        }

        static bool Bool(Dictionary<string, object> d, string key)
        {
            object v;
            if (string.IsNullOrEmpty(key) || !d.TryGetValue(key, out v) || v == null) return false;
            if (v is bool) return (bool)v;
            bool b;
            return bool.TryParse(v.ToString(), out b) && b;
        }

        static long Long(Dictionary<string, object> d, string key)
        {
            object v;
            if (!d.TryGetValue(key, out v) || v == null) return -1;
            if (v is double) return (long)(double)v;
            long l;
            return long.TryParse(v.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out l) ? l : -1;
        }
    }
}
