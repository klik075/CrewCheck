using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using WhoisntCitizen.Lobby;
using WhoisntCitizen.Network;

namespace WhoisntCitizen.RoomTest
{
    /// <summary>
    /// [테스트 전용 - Assets/99_Test/RoomTest 폴더째 삭제 예정]
    ///
    /// RoomTest 씬(Room 씬 복사본)을 서버 없이 가짜 데이터로 돌린다.
    ///
    /// 원리
    ///   1) 에디터 안에 가짜 HTTP 서버(FakeRoomServer)를 띄우고 ApiConfig.BaseUrl을 그 주소로 바꾼다.
    ///   2) 가짜 로그인(AuthSession)과 가짜 방(RoomSession)을 채운다.
    ///   3) 씬에 있는 RoomUIController, GameChatController는 Room 씬과 똑같이(코드 수정 없이) 동작하고,
    ///      서버 대신 이 컴포넌트의 가짜 데이터(Inspector)를 받아 간다.
    ///   4) 씬을 벗어나거나 플레이를 멈추면 BaseUrl과 세션을 원래대로 되돌린다.
    ///
    /// 분리 원칙
    ///   - 메인 코드(02_Scripts 등)는 이 폴더의 어떤 타입도 참조하지 않는다. (이 폴더 → 메인 방향으로만 참조)
    ///   - RoomTest 씬은 Build Settings에 넣지 않는다.
    ///   - 그래서 Assets/99_Test/RoomTest 폴더를 지우면 흔적 없이 사라진다.
    ///
    /// LobbyTest 연동 (LobbyTest 폴더의 타입은 참조하지 않음, JSON과 씬 경로로만 연결)
    ///   - LobbyTest에서 방에 들어와 이 씬이 열렸으면, 넘겨받은 방(TestRoomHandoff)을 Inspector 값 대신 쓴다.
    ///   - 이 씬을 벗어날 때 지금 방 상태를 돌려주고, [나가기]로 로비에 가면 RoomTestSceneRouter가 LobbyTest로 보낸다.
    ///   - LobbyTest가 없거나 이 씬만 단독으로 플레이하면 지금처럼 Inspector 값으로 동작한다.
    ///
    /// 다른 컴포넌트보다 먼저 실행되어야 하므로(RoomUIController.Start가 세션을 확인함) 실행 순서를 가장 앞으로 둔다.
    /// </summary>
    [DefaultExecutionOrder(-10000)]
    [DisallowMultipleComponent]
    public class RoomTestBootstrap : MonoBehaviour
    {
        [Header("가짜 방 데이터 (플레이 중 수정 → 다음 polling에 반영)")]
        [SerializeField] private FakeRoomData room = new FakeRoomData();

        [Header("처음 채팅 기록 (플레이 시작 시 한 번 들어감)")]
        [SerializeField] private List<FakeChatLine> initialChat = FakeChatDefaults.Create();

        [Header("가짜 서버 동작")]
        [Tooltip("0이면 빈 포트를 자동 선택")]
        [SerializeField] private int port;
        [Tooltip("가짜 로그인 토큰의 유효 시간(분). 20분 미만으로 두면 '로그인 유지 시간' 경고와 시작 차단을 확인할 수 있다.")]
        [SerializeField] private int fakeTokenMinutes = 180;
        [Tooltip("켜면 모든 요청에 503을 돌려준다 (서버 오류 화면 확인용)")]
        [SerializeField] private bool failAllRequests;
        [Tooltip("0보다 크면 이 간격(초)마다 다른 참가자가 무작위 채팅을 보낸다")]
        [SerializeField] private float botChatIntervalSeconds;
        [Tooltip("가짜 서버가 받은 요청을 Console에 찍는다")]
        [SerializeField] private bool logRequests;

        [Header("테스트 채팅 보내기 (우클릭 메뉴 '테스트 채팅 보내기')")]
        [SerializeField] private long testChatSenderUserId = 2;
        [SerializeField] private FakeChatType testChatType = FakeChatType.USER;
        [SerializeField] private string testChatMessage = "테스트 메시지입니다.";

        private const string LogTag = "[RoomTest]";

        private static readonly Regex RoomRoute = new Regex(@"^/api/v1/rooms/(\d+)(/.*)?$", RegexOptions.Compiled);

        private readonly FakeRoomServer server = new FakeRoomServer();
        private readonly List<ChatEntry> chat = new List<ChatEntry>();
        private long nextMessageId = 1;
        private string originalBaseUrl;
        private string fakeToken;
        private float nextBotChatAt;
        private bool started;

        /// <summary>서버에 저장된 채팅 한 줄 (서버 ChatMessage에 해당)</summary>
        private sealed class ChatEntry
        {
            public long id;
            public FakeChatType type;
            public long userId;
            public string nickname;
            public string message;
            public DateTime createdAt;
        }

        [Serializable] private class MessageBody { public string message; }

        // ------------------------------------------------------------------
        // 생명주기
        // ------------------------------------------------------------------

        private void Awake()
        {
            if (!Application.isEditor)
            {
                Debug.LogError($"{LogTag} 에디터 전용 테스트입니다. 빌드에서는 동작하지 않습니다.");
                enabled = false;
                return;
            }

            try
            {
                server.Start(port);
            }
            catch (Exception e)
            {
                Debug.LogError($"{LogTag} 가짜 서버를 시작하지 못했습니다: {e.Message}");
                enabled = false;
                return;
            }

            originalBaseUrl = ApiConfig.BaseUrl;
            ApiConfig.BaseUrl = server.BaseUrl;

            // LobbyTest에서 방에 들어온 경우: 그 방과 내 정보로 바꾼다.
            TestRoomHandoff incoming = TestRoomHandoff.Take(TestRoomHandoff.EnterRoomKey);
            bool fromLobbyTest = incoming != null && incoming.room != null;
            if (fromLobbyTest) Adopt(incoming);

            ApplyFakeLogin();
            RoomSession.Clear();
            RoomSession.Set(BuildRoomDetail()); // RoomUIController.Start가 확인하는 방 정보

            chat.Clear();
            if (fromLobbyTest)
            {
                foreach (FakePlayer p in room.players)
                    AddChat(FakeChatType.SYSTEM, 0, null, $"{p.nickname}님이 입장했습니다.");
            }
            else
            {
                foreach (FakeChatLine line in initialChat)
                    if (line != null) AddChat(line.type, line.userId, line.nickname, line.message);
            }

            nextBotChatAt = Time.unscaledTime + botChatIntervalSeconds;
            started = true;
            RoomTestSceneRouter.Arm();
            if (fromLobbyTest) Debug.Log($"{LogTag} LobbyTest에서 넘겨받은 방으로 시작합니다: #{room.roomId} {room.title}");
            Debug.Log($"{LogTag} 가짜 서버 시작: {server.BaseUrl}  (원래 서버 주소 {originalBaseUrl}는 씬을 벗어나면 복구)\n" +
                      $"나: {room.NicknameOf(room.myUserId)}(userId {room.myUserId}), 방장 userId {room.hostUserId}, 참가자 {room.players.Count}명");
        }

        private void Update()
        {
            if (!started) return;

            // Inspector에서 '나'를 바꾸면 로그인 정보도 맞춘다. (RoomUIController가 AuthSession.UserId로 나를 찾음)
            if (AuthSession.AccessToken == fakeToken && AuthSession.UserId != room.myUserId) ApplyFakeLogin();

            server.Pump(Handle);

            if (botChatIntervalSeconds > 0f && Time.unscaledTime >= nextBotChatAt)
            {
                nextBotChatAt = Time.unscaledTime + botChatIntervalSeconds;
                SendRandomBotChat();
            }
        }

        private void OnDestroy()
        {
            if (!started) return;
            started = false;
            server.Dispose();

            // LobbyTest로 돌아가면 반영할 수 있도록 지금 방 상태를 남긴다. (LobbyTest가 없으면 아무도 읽지 않고 다음 플레이에 비워짐)
            TestRoomHandoff.Put(TestRoomHandoff.LeaveRoomKey, new TestRoomHandoff
            {
                myUserId = room.myUserId,
                myNickname = room.NicknameOf(room.myUserId),
                room = BuildRoomDetail(),
            });

            // 다른 씬(Lobby 등)으로 넘어가면 가짜 설정이 새지 않도록 원래대로 되돌린다.
            if (ApiConfig.BaseUrl == server.BaseUrl) ApiConfig.BaseUrl = originalBaseUrl;
            if (AuthSession.AccessToken == fakeToken) AuthSession.Clear();
            if (RoomSession.RoomId == room.roomId) RoomSession.Clear();
            Debug.Log($"{LogTag} 가짜 서버 종료, 서버 주소를 {originalBaseUrl}(으)로 복구했습니다.");
        }

        // ------------------------------------------------------------------
        // 요청 처리 (실제 서버 RoomController / ChatMessageController와 같은 규칙)
        // ------------------------------------------------------------------

        private FakeHttpResponse Handle(FakeHttpRequest req)
        {
            FakeHttpResponse res = Route(req);
            if (logRequests)
                Debug.Log($"{LogTag} {req.Method} {req.Path}{(string.IsNullOrEmpty(req.Query) ? "" : "?" + req.Query)} → {res.Status}");
            return res;
        }

        private FakeHttpResponse Route(FakeHttpRequest req)
        {
            if (failAllRequests)
                return FakeHttpResponse.Error(503, "FAKE_SERVER_DOWN", "[RoomTest] failAllRequests가 켜져 있어 모든 요청을 실패시킵니다.");

            if (req.Authorization != "Bearer " + fakeToken)
                return new FakeHttpResponse { Status = 401, Body = null }; // 실제 서버도 401은 본문 없음

            Match m = RoomRoute.Match(req.Path);
            if (!m.Success) return NotImplemented(req);

            long roomId = long.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            if (roomId != room.roomId)
                return FakeHttpResponse.Error(400, "BAD_REQUEST", $"방이 존재하지 않습니다. (roomId={roomId})");

            string sub = m.Groups[2].Success ? m.Groups[2].Value : "";
            switch (req.Method + " " + sub)
            {
                case "GET ": return FakeHttpResponse.Json(200, JsonUtility.ToJson(BuildRoomDetail()));
                case "GET /players": return FakeHttpResponse.Json(200, PlayersJson());
                case "DELETE /players/me": return Leave();
                case "PUT /players/me/ready": return SetReady(req.Body);
                case "PUT /host": return TransferHost(req.Body);
                case "POST /games": return StartGame();
                case "GET /messages": return GetMessages(req);
                case "POST /messages": return PostMessage(req.Body, FakeChatType.USER);
                case "POST /system-messages": return PostMessage(req.Body, FakeChatType.SYSTEM);
            }

            Match kick = Regex.Match(sub, @"^/players/(\d+)$");
            if (req.Method == "DELETE" && kick.Success)
                return Kick(long.Parse(kick.Groups[1].Value, CultureInfo.InvariantCulture));

            return NotImplemented(req);
        }

        private FakeHttpResponse NotImplemented(FakeHttpRequest req)
        {
            Debug.LogWarning($"{LogTag} 가짜 서버에 없는 API입니다: {req.Method} {req.Path}");
            return FakeHttpResponse.Error(404, "NOT_FOUND", $"[RoomTest] 가짜 서버에 없는 API: {req.Method} {req.Path}");
        }

        private FakeHttpResponse Leave()
        {
            FakePlayer me = room.Find(room.myUserId);
            if (me == null) return FakeHttpResponse.Error(409, "CONFLICT", "참가 중인 방이 아닙니다.");

            room.players.Remove(me);
            AddChat(FakeChatType.SYSTEM, 0, null, $"{me.nickname}님이 퇴장했습니다.");
            if (room.hostUserId == me.userId && room.players.Count > 0) PromoteHost(room.players[0]);
            return FakeHttpResponse.NoContent();
        }

        private FakeHttpResponse SetReady(string body)
        {
            FakePlayer me = room.Find(room.myUserId);
            if (me == null) return FakeHttpResponse.Error(409, "CONFLICT", "참가 중인 방이 아닙니다.");
            if (room.hostUserId == me.userId) return FakeHttpResponse.Error(409, "CONFLICT", "방장은 준비할 수 없습니다.");

            SetReadyRequest parsed = Parse<SetReadyRequest>(body);
            if (parsed == null) return FakeHttpResponse.Error(400, "BAD_REQUEST", "요청 본문을 읽을 수 없습니다.");
            me.ready = parsed.ready;
            return FakeHttpResponse.NoContent();
        }

        private FakeHttpResponse TransferHost(string body)
        {
            if (room.hostUserId != room.myUserId) return FakeHttpResponse.Error(403, "FORBIDDEN", "방장만 방장을 위임할 수 있습니다.");

            TransferHostRequest parsed = Parse<TransferHostRequest>(body);
            if (parsed == null) return FakeHttpResponse.Error(400, "BAD_REQUEST", "요청 본문을 읽을 수 없습니다.");
            if (parsed.userId == room.myUserId) return FakeHttpResponse.Error(400, "BAD_REQUEST", "자기 자신에게 위임할 수 없습니다.");

            FakePlayer target = room.Find(parsed.userId);
            if (target == null) return FakeHttpResponse.Error(409, "CONFLICT", "대상이 방에 없습니다.");

            FakePlayer me = room.Find(room.myUserId);
            if (me != null) me.ready = false; // 이전 방장은 준비 안 한 일반 참가자가 된다
            PromoteHost(target);
            return FakeHttpResponse.NoContent();
        }

        private FakeHttpResponse Kick(long targetUserId)
        {
            if (room.hostUserId != room.myUserId) return FakeHttpResponse.Error(403, "FORBIDDEN", "방장만 추방할 수 있습니다.");
            if (targetUserId == room.myUserId) return FakeHttpResponse.Error(400, "BAD_REQUEST", "자기 자신은 추방할 수 없습니다.");

            FakePlayer target = room.Find(targetUserId);
            if (target == null) return FakeHttpResponse.Error(409, "CONFLICT", "대상이 방에 없습니다.");

            room.players.Remove(target);
            if (!room.kickedUserIds.Contains(targetUserId)) room.kickedUserIds.Add(targetUserId);
            AddChat(FakeChatType.SYSTEM, 0, null, $"{target.nickname}님이 추방되었습니다.");
            return FakeHttpResponse.NoContent();
        }

        private FakeHttpResponse StartGame()
        {
            if (room.hostUserId != room.myUserId) return FakeHttpResponse.Error(403, "FORBIDDEN", "방장만 게임을 시작할 수 있습니다.");
            if (room.players.Count < 4) return FakeHttpResponse.Error(409, "CONFLICT", "게임을 시작하려면 최소 4명이 필요합니다.");
            foreach (FakePlayer p in room.players)
                if (p.userId != room.hostUserId && !p.ready)
                    return FakeHttpResponse.Error(409, "CONFLICT", "준비하지 않은 참가자가 있습니다.");

            // 실제로 게임을 만들면 GameScene으로 넘어가 진짜 서버를 찾게 되므로, 조건 검사까지만 하고 멈춘다.
            Debug.Log($"{LogTag} 게임 시작 조건을 모두 통과했습니다. (가짜 서버라 GameScene으로는 이동하지 않음)");
            return FakeHttpResponse.Error(409, "ROOM_TEST", "[테스트] 시작 조건 통과! 가짜 서버라 실제 게임은 시작하지 않습니다.");
        }

        private FakeHttpResponse GetMessages(FakeHttpRequest req)
        {
            int limit = 50;
            string limitText = req.QueryValue("limit");
            if (!string.IsNullOrEmpty(limitText) && int.TryParse(limitText, out int l)) limit = Mathf.Clamp(l, 1, 200);

            var result = new List<ChatEntry>();
            string afterText = req.QueryValue("afterId");
            if (!string.IsNullOrEmpty(afterText) && long.TryParse(afterText, out long afterId))
            {
                foreach (ChatEntry e in chat)
                    if (e.id > afterId && result.Count < limit) result.Add(e);
            }
            else
            {
                for (int i = Mathf.Max(0, chat.Count - limit); i < chat.Count; i++) result.Add(chat[i]);
            }

            var sb = new StringBuilder("[");
            for (int i = 0; i < result.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(MessageJson(result[i]));
            }
            return FakeHttpResponse.Json(200, sb.Append(']').ToString());
        }

        private FakeHttpResponse PostMessage(string body, FakeChatType type)
        {
            MessageBody parsed = Parse<MessageBody>(body);
            string text = parsed != null ? parsed.message?.Trim() : null;
            if (string.IsNullOrEmpty(text)) return FakeHttpResponse.Error(400, "BAD_REQUEST", "메시지를 입력해 주세요.");

            if (type == FakeChatType.SYSTEM) return FakeHttpResponse.Json(201, MessageJson(AddChat(FakeChatType.SYSTEM, 0, null, text)));

            FakePlayer me = room.Find(room.myUserId);
            if (me == null) return FakeHttpResponse.Error(403, "FORBIDDEN", "현재 채팅할 수 없는 플레이어입니다. (방 참가자가 아님)");
            return FakeHttpResponse.Json(201, MessageJson(AddChat(FakeChatType.USER, me.userId, me.nickname, text)));
        }

        // ------------------------------------------------------------------
        // 데이터 → JSON
        // ------------------------------------------------------------------

        /// <summary>서버 GET /api/v1/rooms/{roomId} 응답과 같은 모양 (메인 DTO를 그대로 사용)</summary>
        private RoomDetailResponse BuildRoomDetail()
        {
            var detail = new RoomDetailResponse
            {
                id = room.roomId,
                title = room.title,
                hostUserId = room.hostUserId,
                maxPlayers = room.maxPlayers,
                currentPlayers = room.players.Count,
                status = RoomStatus.Waiting,
                gameId = "",
                privateRoom = room.privateRoom,
                players = new List<RoomPlayerResponse>(),
                kickedUserIds = new List<long>(room.kickedUserIds),
            };
            foreach (FakePlayer p in room.players)
                if (p != null) detail.players.Add(new RoomPlayerResponse { userId = p.userId, nickname = p.nickname, ready = p.ready });
            return detail;
        }

        private string PlayersJson()
        {
            var sb = new StringBuilder("[");
            for (int i = 0; i < room.players.Count; i++)
            {
                FakePlayer p = room.players[i];
                if (i > 0) sb.Append(',');
                sb.Append("{\"userId\":").Append(p.userId)
                  .Append(",\"nickname\":").Append(FakeJson.Quote(p.nickname))
                  .Append(",\"ready\":").Append(p.ready ? "true" : "false").Append('}');
            }
            return sb.Append(']').ToString();
        }

        /// <summary>서버 ChatMessageSummary / ChatMessageResponse와 같은 키</summary>
        private static string MessageJson(ChatEntry e)
        {
            return "{\"messageId\":" + e.id +
                   ",\"type\":" + FakeJson.Quote(e.type.ToString()) +
                   ",\"userId\":" + e.userId +
                   ",\"nickname\":" + FakeJson.Quote(e.nickname) +
                   ",\"message\":" + FakeJson.Quote(e.message) +
                   ",\"createdAt\":" + FakeJson.Quote(e.createdAt.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture)) +
                   ",\"nightChat\":false}";
        }

        // ------------------------------------------------------------------
        // 도우미
        // ------------------------------------------------------------------

        private ChatEntry AddChat(FakeChatType type, long userId, string nickname, string message)
        {
            bool system = type == FakeChatType.SYSTEM;
            var e = new ChatEntry
            {
                id = nextMessageId++,
                type = type,
                userId = system ? 0 : userId,
                nickname = system ? "SYSTEM" : (string.IsNullOrEmpty(nickname) ? room.NicknameOf(userId) : nickname),
                message = message ?? "",
                createdAt = DateTime.Now,
            };
            chat.Add(e);
            return e;
        }

        /// <summary>LobbyTest에서 넘겨받은 방으로 가짜 데이터를 바꾼다.</summary>
        private void Adopt(TestRoomHandoff h)
        {
            RoomDetailResponse d = h.room;
            room.roomId = d.id;
            room.title = d.title;
            room.maxPlayers = d.maxPlayers;
            room.privateRoom = d.privateRoom;
            room.hostUserId = d.hostUserId;
            room.myUserId = h.myUserId;
            room.players = new List<FakePlayer>();
            if (d.players != null)
                foreach (RoomPlayerResponse p in d.players) room.players.Add(new FakePlayer(p.userId, p.nickname, p.ready));
            room.kickedUserIds = d.kickedUserIds != null ? new List<long>(d.kickedUserIds) : new List<long>();
        }

        private void PromoteHost(FakePlayer newHost)
        {
            room.hostUserId = newHost.userId;
            newHost.ready = false;
            AddChat(FakeChatType.SYSTEM, 0, null, $"{newHost.nickname}님이 방장이 되었습니다.");
        }

        private void ApplyFakeLogin()
        {
            fakeToken = MakeFakeJwt(room.myUserId, fakeTokenMinutes);
            AuthSession.SetSession(1000 + room.myUserId, room.myUserId, "roomtest" + room.myUserId,
                room.NicknameOf(room.myUserId), fakeToken);
        }

        /// <summary>서명 없는 가짜 JWT. AuthSession이 exp - iat로 남은 시간을 계산하므로 두 값만 넣는다.</summary>
        private static string MakeFakeJwt(long userId, int minutes)
        {
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            string header = Base64Url("{\"alg\":\"none\",\"typ\":\"JWT\"}");
            string payload = Base64Url("{\"sub\":\"" + (1000 + userId) + "\",\"iat\":" + now + ",\"exp\":" + (now + Math.Max(1, minutes) * 60L) + "}");
            return header + "." + payload + ".roomtest";
        }

        private static string Base64Url(string text)
        {
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(text)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        private static T Parse<T>(string json) where T : class
        {
            if (string.IsNullOrEmpty(json)) return null;
            try { return JsonUtility.FromJson<T>(json); }
            catch (Exception) { return null; }
        }

        private void SendRandomBotChat()
        {
            var others = new List<FakePlayer>();
            foreach (FakePlayer p in room.players) if (p != null && p.userId != room.myUserId) others.Add(p);
            if (others.Count == 0) return;
            FakePlayer who = others[UnityEngine.Random.Range(0, others.Count)];
            string line = FakeChatDefaults.RandomLines[UnityEngine.Random.Range(0, FakeChatDefaults.RandomLines.Length)];
            AddChat(FakeChatType.USER, who.userId, who.nickname, line);
        }

        // ------------------------------------------------------------------
        // Inspector 우클릭 메뉴 (플레이 중에 사용)
        // ------------------------------------------------------------------

        [ContextMenu("테스트 채팅 보내기")]
        private void MenuSendTestChat()
        {
            if (!RequirePlaying()) return;
            AddChat(testChatType, testChatSenderUserId, null, testChatMessage);
        }

        [ContextMenu("다른 참가자가 무작위 채팅")]
        private void MenuRandomChat()
        {
            if (!RequirePlaying()) return;
            SendRandomBotChat();
        }

        [ContextMenu("가짜 참가자 입장")]
        private void MenuAddPlayer()
        {
            if (!RequirePlaying()) return;
            if (room.players.Count >= room.maxPlayers) { Debug.LogWarning($"{LogTag} 정원이 가득 찼습니다."); return; }
            long id = 1;
            foreach (FakePlayer p in room.players) id = Math.Max(id, p.userId + 1);
            var added = new FakePlayer(id, "손님" + id, false);
            room.players.Add(added);
            AddChat(FakeChatType.SYSTEM, 0, null, $"{added.nickname}님이 입장했습니다.");
        }

        [ContextMenu("마지막 참가자 퇴장 (나 제외)")]
        private void MenuRemoveLastPlayer()
        {
            if (!RequirePlaying()) return;
            for (int i = room.players.Count - 1; i >= 0; i--)
            {
                FakePlayer p = room.players[i];
                if (p.userId == room.myUserId) continue;
                room.players.RemoveAt(i);
                AddChat(FakeChatType.SYSTEM, 0, null, $"{p.nickname}님이 퇴장했습니다.");
                if (room.hostUserId == p.userId && room.players.Count > 0) PromoteHost(room.players[0]);
                return;
            }
        }

        [ContextMenu("다른 참가자 모두 준비 ↔ 해제")]
        private void MenuToggleAllReady()
        {
            if (!RequirePlaying()) return;
            bool allReady = true;
            foreach (FakePlayer p in room.players)
                if (p.userId != room.hostUserId && p.userId != room.myUserId && !p.ready) allReady = false;
            foreach (FakePlayer p in room.players)
                if (p.userId != room.hostUserId && p.userId != room.myUserId) p.ready = !allReady;
        }

        [ContextMenu("방장 바꾸기 (나 ↔ 다음 참가자)")]
        private void MenuSwapHost()
        {
            if (!RequirePlaying()) return;
            if (room.hostUserId != room.myUserId)
            {
                FakePlayer me = room.Find(room.myUserId);
                if (me != null) PromoteHost(me);
                return;
            }
            foreach (FakePlayer p in room.players)
                if (p.userId != room.myUserId) { PromoteHost(p); return; }
        }

        private bool RequirePlaying()
        {
            if (started) return true;
            Debug.LogWarning($"{LogTag} 플레이 중에만 사용할 수 있습니다.");
            return false;
        }
    }
}
