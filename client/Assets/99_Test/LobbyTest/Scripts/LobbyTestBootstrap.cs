using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using WhoisntCitizen.Common;
using WhoisntCitizen.Lobby;
using WhoisntCitizen.Network;

namespace WhoisntCitizen.TestScenes.LobbyTest
{
    /// <summary>
    /// [테스트 전용 - Assets/99_Test/LobbyTest 폴더째 삭제 예정]
    ///
    /// LobbyTest 씬(Lobby 씬 복사본)을 서버 없이 가짜 데이터로 돌린다.
    ///
    /// 원리
    ///   1) 에디터 안에 가짜 HTTP 서버(FakeLobbyServer)를 띄우고 ApiConfig.BaseUrl을 그 주소로 바꾼다.
    ///   2) 가짜 로그인(AuthSession)을 채운다.
    ///   3) 씬에 있는 LobbyUIController / CreateRoomPopup / RoomPasswordPopup은 Lobby 씬과 똑같이(코드 수정 없이) 동작하고,
    ///      서버 대신 이 컴포넌트의 가짜 방 목록(Inspector)을 받아 간다.
    ///   4) 방 입장/생성에 성공하면 LobbyTestSceneRouter가 진짜 Room 씬 대신 RoomTest 씬으로 보낸다.
    ///      이때 입장한 방 정보를 넘겨주고(TestRoomHandoff), RoomTest에서 나오면 바뀐 방 정보를 돌려받는다.
    ///   5) 씬을 벗어나면 BaseUrl과 로그인을 원래대로 되돌린다. 방 목록은 플레이가 끝날 때까지 기억한다.
    ///
    /// 분리 원칙
    ///   - 메인 코드는 이 폴더의 어떤 타입도 참조하지 않는다. (이 폴더 → 메인 방향으로만 참조)
    ///   - RoomTest 폴더의 타입도 참조하지 않는다. (씬 경로 문자열과 JSON으로만 연결, RoomTest를 지워도 동작)
    ///   - LobbyTest 씬은 Build Settings에 넣지 않는다.
    /// </summary>
    [DefaultExecutionOrder(-10000)]
    [DisallowMultipleComponent]
    public class LobbyTestBootstrap : MonoBehaviour
    {
        [Header("가짜 로비 데이터 (플레이 중 수정 → [새로고침]을 누르면 반영)")]
        [SerializeField] private FakeLobbyData data = new FakeLobbyData();

        [Header("가짜 서버 동작")]
        [Tooltip("0이면 빈 포트를 자동 선택")]
        [SerializeField] private int port;
        [Tooltip("응답을 이만큼(초) 늦게 보낸다. '불러오는 중...', 버튼 잠금 같은 로딩 상태 확인용")]
        [SerializeField, Range(0f, 5f)] private float responseDelaySeconds;
        [Tooltip("켜면 모든 요청에 503을 돌려준다 (서버 오류 화면 확인용)")]
        [SerializeField] private bool failAllRequests;
        [Tooltip("가짜 로그인 토큰의 유효 시간(분)")]
        [SerializeField] private int fakeTokenMinutes = 180;
        [Tooltip("가짜 서버가 받은 요청을 Console에 찍는다")]
        [SerializeField] private bool logRequests;

        private const string LogTag = "[LobbyTest]";
        private const string StateKey = "WhoisntCitizen.LobbyTest.State"; // 씬을 오가도 방 목록을 기억 (플레이 중에만)
        private const int SearchKeywordMaxLength = 30;

        private static readonly Regex RoomRoute = new Regex(@"^/api/v1/rooms/(\d+)(/players)?$", RegexOptions.Compiled);

        private readonly FakeLobbyServer server = new FakeLobbyServer();
        private string originalBaseUrl;
        private string fakeToken;
        private bool started;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            AppDomain.CurrentDomain.SetData(StateKey, null);
        }

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

            RestoreState();

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
            ApplyFakeLogin(); // LobbyUIController.Start가 로그인 여부를 확인한다
            SceneLoader.LoadStarted += OnSceneLoadStarted;
            LobbyTestSceneRouter.Arm();
            started = true;

            Debug.Log($"{LogTag} 가짜 서버 시작: {server.BaseUrl}  (원래 서버 주소 {originalBaseUrl}는 씬을 벗어나면 복구)\n" +
                      $"나: {data.myNickname}(userId {data.myUserId}), 방 {data.rooms.Count}개");
        }

        private void Update()
        {
            if (!started) return;
            if (AuthSession.AccessToken == fakeToken && AuthSession.UserId != data.myUserId) ApplyFakeLogin(); // Inspector에서 나를 바꾼 경우
            server.Pump(Handle, responseDelaySeconds);
        }

        private void OnDestroy()
        {
            if (!started) return;
            started = false;
            SceneLoader.LoadStarted -= OnSceneLoadStarted;
            server.Dispose();
            AppDomain.CurrentDomain.SetData(StateKey, JsonUtility.ToJson(data));

            // 다른 씬으로 넘어가면 가짜 설정이 새지 않도록 원래대로 되돌린다.
            if (ApiConfig.BaseUrl == server.BaseUrl) ApiConfig.BaseUrl = originalBaseUrl;
            if (AuthSession.AccessToken == fakeToken) AuthSession.Clear();
        }

        /// <summary>방 입장/생성 성공으로 Room 씬 이동이 시작될 때, 들어간 방 정보를 RoomTest로 넘긴다.</summary>
        private void OnSceneLoadStarted(SceneType type)
        {
            if (type != SceneType.Room) return;
            FakeLobbyRoom room = data.Find(RoomSession.RoomId);
            if (room == null) return;
            TestRoomHandoff.Put(TestRoomHandoff.EnterRoomKey, new TestRoomHandoff
            {
                myUserId = data.myUserId,
                myNickname = data.myNickname,
                room = room.ToDetail(data.myUserId),
            });
        }

        // ------------------------------------------------------------------
        // 씬을 오갈 때 상태 이어받기
        // ------------------------------------------------------------------

        private void RestoreState()
        {
            string saved = AppDomain.CurrentDomain.GetData(StateKey) as string;
            if (!string.IsNullOrEmpty(saved))
            {
                try { data = JsonUtility.FromJson<FakeLobbyData>(saved); }
                catch (Exception e) { Debug.LogWarning($"{LogTag} 이전 방 목록을 읽지 못해 Inspector 값으로 시작합니다: {e.Message}"); }
            }

            // RoomTest가 받아 가지 않은 입장 정보 = RoomTest로 가지 못하고 돌아온 경우 → 그 방에서 나간 것으로 처리
            TestRoomHandoff unused = TestRoomHandoff.Take(TestRoomHandoff.EnterRoomKey);
            if (unused != null && unused.room != null)
            {
                FakeLobbyRoom room = data.Find(unused.room.id);
                if (room != null) RemovePlayer(room, data.myUserId);
            }

            // RoomTest에서 나오면서 돌려준 방 상태 반영 (참가자, 방장, 추방 목록 등)
            TestRoomHandoff back = TestRoomHandoff.Take(TestRoomHandoff.LeaveRoomKey);
            if (back != null && back.room != null) Merge(back);
        }

        private void Merge(TestRoomHandoff back)
        {
            RoomDetailResponse d = back.room;
            FakeLobbyRoom room = data.Find(d.id);
            if (d.players == null || d.players.Count == 0)
            {
                if (room != null) data.rooms.Remove(room); // 마지막 사람이 나가면 서버는 방을 지운다
                return;
            }
            if (room == null)
            {
                room = new FakeLobbyRoom { id = d.id, privateRoom = d.privateRoom };
                data.rooms.Add(room);
            }
            room.title = d.title;
            room.maxPlayers = d.maxPlayers;
            room.hostUserId = d.hostUserId;
            room.status = FakeRoomStatus.WAITING;
            room.iAmKicked = d.kickedUserIds != null && d.kickedUserIds.Contains(data.myUserId);
            room.players.Clear();
            foreach (RoomPlayerResponse p in d.players) room.players.Add(new FakeLobbyPlayer(p.userId, p.nickname, p.ready));
        }

        // ------------------------------------------------------------------
        // 요청 처리 (실제 서버 RoomController와 같은 규칙)
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
                return FakeHttpResponse.Error(503, "FAKE_SERVER_DOWN", "[LobbyTest] failAllRequests가 켜져 있어 모든 요청을 실패시킵니다.");

            if (req.Authorization != "Bearer " + fakeToken)
                return new FakeHttpResponse { Status = 401, Body = null }; // 실제 서버도 401은 본문 없음

            if (req.Path == "/api/v1/rooms")
            {
                if (req.Method == "GET") return GetRooms(req.QueryValue("keyword"));
                if (req.Method == "POST") return CreateRoom(req.Body);
                return NotImplemented(req);
            }

            Match m = RoomRoute.Match(req.Path);
            if (!m.Success) return NotImplemented(req);

            long roomId = long.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            FakeLobbyRoom room = data.Find(roomId);
            if (room == null) return FakeHttpResponse.Error(400, "BAD_REQUEST", $"방이 존재하지 않습니다. (roomId={roomId})");

            bool players = m.Groups[2].Success;
            if (!players && req.Method == "GET") return FakeHttpResponse.Json(200, JsonUtility.ToJson(room.ToDetail(data.myUserId)));
            if (players && req.Method == "GET") return FakeHttpResponse.Json(200, PlayersJson(room));
            if (players && req.Method == "POST") return JoinRoom(room, req.Body);
            return NotImplemented(req);
        }

        private FakeHttpResponse NotImplemented(FakeHttpRequest req)
        {
            Debug.LogWarning($"{LogTag} 가짜 서버에 없는 API입니다: {req.Method} {req.Path}");
            return FakeHttpResponse.Error(404, "NOT_FOUND", $"[LobbyTest] 가짜 서버에 없는 API: {req.Method} {req.Path}");
        }

        /// <summary>GET /api/v1/rooms[?keyword=] : 제목 부분 일치, 대소문자·공백 무시, 공백을 뺀 검색어 30자 초과면 400</summary>
        private FakeHttpResponse GetRooms(string keyword)
        {
            string key = Normalize(keyword);
            if (key.Length > SearchKeywordMaxLength)
                return FakeHttpResponse.Error(400, "BAD_REQUEST", $"검색어는 공백을 제외하고 {SearchKeywordMaxLength}자 이하로 입력해 주세요.");

            var sb = new StringBuilder("[");
            bool first = true;
            foreach (FakeLobbyRoom room in data.rooms)
            {
                if (room == null || room.players.Count == 0) continue; // 빈 방은 서버에 남지 않는다
                if (key.Length > 0 && !Normalize(room.title).Contains(key)) continue;
                if (!first) sb.Append(',');
                sb.Append(JsonUtility.ToJson(room.ToResponse()));
                first = false;
            }
            return FakeHttpResponse.Json(200, sb.Append(']').ToString());
        }

        /// <summary>POST /api/v1/rooms : 만든 사람이 방장으로 자동 입장</summary>
        private FakeHttpResponse CreateRoom(string body)
        {
            CreateRoomRequest r = Parse<CreateRoomRequest>(body);
            if (r == null) return FakeHttpResponse.Error(400, "BAD_REQUEST", "요청 본문을 읽을 수 없습니다.");
            string title = r.title?.Trim();
            if (string.IsNullOrEmpty(title)) return FakeHttpResponse.Error(400, "BAD_REQUEST", "방 제목을 입력하세요.");
            if (r.maxPlayers < 4 || r.maxPlayers > 12) return FakeHttpResponse.Error(400, "BAD_REQUEST", "최대 인원은 4~12명이어야 합니다.");
            if (r.privateRoom && !RoomPasswordRule.IsValid(r.password))
                return FakeHttpResponse.Error(400, "BAD_REQUEST", $"비밀번호는 {RoomPasswordRule.Description}이어야 합니다.");

            var room = new FakeLobbyRoom
            {
                id = data.NextRoomId(),
                title = title,
                maxPlayers = r.maxPlayers,
                privateRoom = r.privateRoom,
                password = r.privateRoom ? r.password : null,
                hostUserId = data.myUserId,
            };
            room.players.Add(new FakeLobbyPlayer(data.myUserId, data.myNickname, false));
            data.rooms.Add(room);
            Debug.Log($"{LogTag} 방 생성: #{room.id} {room.title}");
            return FakeHttpResponse.Json(201, JsonUtility.ToJson(room.ToResponse()));
        }

        /// <summary>POST /api/v1/rooms/{id}/players : 비밀방이면 body {"password"}</summary>
        private FakeHttpResponse JoinRoom(FakeLobbyRoom room, string body)
        {
            if (room.iAmKicked)
                return FakeHttpResponse.Error(403, RoomErrorCode.KickedFromRoom, "방장에 의해 추방된 방에는 다시 입장할 수 없습니다.");
            if (room.status == FakeRoomStatus.IN_GAME)
                return FakeHttpResponse.Error(409, "CONFLICT", "게임이 진행 중인 방에는 입장할 수 없습니다.");
            if (room.Find(data.myUserId) != null)
                return FakeHttpResponse.Error(409, "CONFLICT", "이미 참가 중인 방입니다.");
            if (room.players.Count >= room.maxPlayers)
                return FakeHttpResponse.Error(409, "CONFLICT", "방 인원이 가득 찼습니다.");
            if (room.privateRoom)
            {
                JoinRoomRequest r = Parse<JoinRoomRequest>(body);
                if (r == null || string.IsNullOrEmpty(r.password) || r.password != room.password)
                    return FakeHttpResponse.Error(403, RoomErrorCode.WrongRoomPassword, "비밀번호가 일치하지 않습니다.");
            }

            room.players.Add(new FakeLobbyPlayer(data.myUserId, data.myNickname, false));
            Debug.Log($"{LogTag} 방 입장: #{room.id} {room.title} ({room.players.Count}/{room.maxPlayers})");
            return FakeHttpResponse.Json(200, JsonUtility.ToJson(room.ToResponse()));
        }

        // ------------------------------------------------------------------
        // 도우미
        // ------------------------------------------------------------------

        private static string PlayersJson(FakeLobbyRoom room)
        {
            var sb = new StringBuilder("[");
            for (int i = 0; i < room.players.Count; i++)
            {
                FakeLobbyPlayer p = room.players[i];
                if (i > 0) sb.Append(',');
                sb.Append("{\"userId\":").Append(p.userId)
                  .Append(",\"nickname\":").Append(FakeHttpResponse.Quote(p.nickname))
                  .Append(",\"ready\":").Append(p.ready ? "true" : "false").Append('}');
            }
            return sb.Append(']').ToString();
        }

        private static string Normalize(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new StringBuilder(s.Length);
            foreach (char c in s) if (!char.IsWhiteSpace(c)) sb.Append(char.ToLowerInvariant(c));
            return sb.ToString();
        }

        private void RemovePlayer(FakeLobbyRoom room, long userId)
        {
            FakeLobbyPlayer p = room.Find(userId);
            if (p == null) return;
            room.players.Remove(p);
            if (room.players.Count == 0) data.rooms.Remove(room);
            else if (room.hostUserId == userId) room.hostUserId = room.players[0].userId;
        }

        private void ApplyFakeLogin()
        {
            fakeToken = MakeFakeJwt(data.myUserId, fakeTokenMinutes);
            AuthSession.SetSession(1000 + data.myUserId, data.myUserId, data.myUsername, data.myNickname, fakeToken);
        }

        /// <summary>서명 없는 가짜 JWT. AuthSession이 exp - iat로 남은 시간을 계산하므로 두 값만 넣는다.</summary>
        private static string MakeFakeJwt(long userId, int minutes)
        {
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            string header = Base64Url("{\"alg\":\"none\",\"typ\":\"JWT\"}");
            string payload = Base64Url("{\"sub\":\"" + (1000 + userId) + "\",\"iat\":" + now + ",\"exp\":" + (now + Math.Max(1, minutes) * 60L) + "}");
            return header + "." + payload + ".lobbytest";
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

        // ------------------------------------------------------------------
        // Inspector 우클릭 메뉴 (플레이 중 사용 후 로비의 [새로고침]을 누르면 반영)
        // ------------------------------------------------------------------

        [ContextMenu("가짜 방 하나 추가")]
        private void MenuAddRoom()
        {
            data.rooms.Add(FakeLobbyDefaults.RandomRoom(data.NextRoomId()));
        }

        [ContextMenu("방 목록 비우기 (빈 목록 안내 확인)")]
        private void MenuClearRooms()
        {
            data.rooms.Clear();
        }

        [ContextMenu("방 목록 기본값으로 되돌리기")]
        private void MenuResetRooms()
        {
            data.rooms = FakeLobbyDefaults.CreateRooms();
        }

        [ContextMenu("모든 방에서 나 빼기")]
        private void MenuLeaveAll()
        {
            foreach (FakeLobbyRoom room in new List<FakeLobbyRoom>(data.rooms)) RemovePlayer(room, data.myUserId);
        }
    }
}
