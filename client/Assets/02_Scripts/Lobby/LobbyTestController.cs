using System;
using System.Collections;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

namespace WhoisntCitizen.LobbyTest
{
    // 회원가입/로그인 + 로비(방 목록/생성/입장/퇴장/참가자 조회) 통합 테스트용 컨트롤러.
    // 로그인 토큰은 기존 AuthSession(static)에 저장하고, 로비 요청에는 Authorization: Bearer 헤더를 붙인다.
    public class LobbyTestController : MonoBehaviour
    {
        [Header("Server")]
        [SerializeField] private string baseUrl = "http://localhost:8080";
        [SerializeField] private int timeoutSeconds = 10;

        [Header("Auth UI")]
        [SerializeField] private GameObject authPanel;
        [SerializeField] private TMP_InputField usernameInput;
        [SerializeField] private TMP_InputField passwordInput;
        [SerializeField] private TMP_InputField nicknameInput;
        [SerializeField] private Button signupButton;
        [SerializeField] private Button loginButton;
        [SerializeField] private Button logoutButton;
        [SerializeField] private TextMeshProUGUI authStatusText;

        [Header("Lobby UI")]
        [SerializeField] private GameObject lobbyPanel;
        [SerializeField] private TMP_InputField roomTitleInput;
        [SerializeField] private TMP_InputField maxPlayersInput;
        [SerializeField] private Button createRoomButton;
        [SerializeField] private TMP_InputField roomIdInput;
        [SerializeField] private Button joinRoomButton;
        [SerializeField] private Button leaveRoomButton;
        [SerializeField] private Button playersButton;
        [SerializeField] private Button refreshRoomsButton;
        [SerializeField] private TextMeshProUGUI currentRoomText;
        [SerializeField] private TextMeshProUGUI roomListText;
        [SerializeField] private TextMeshProUGUI playersText;

        [Header("Log")]
        [SerializeField] private TextMeshProUGUI logText;
        [SerializeField] private ScrollRect logScroll;

        private const string SignupPath = "/api/members/signup";
        private const string LoginPath = "/api/members/login";
        private const string RoomsPath = "/api/v1/rooms";
        private const int DefaultMaxPlayers = 6;
        private const int MaxLogChars = 12000;
        private const string OkColor = "#6EE787";
        private const string FailColor = "#FF7B72";

        private readonly StringBuilder log = new StringBuilder();
        private bool isBusy;
        private long currentRoomId; // 입장/생성한 방 (0 = 없음)

        private void Awake()
        {
            if (passwordInput != null)
            {
                passwordInput.contentType = TMP_InputField.ContentType.Password;
                passwordInput.ForceLabelUpdate();
            }
        }

        // 에디터 툴이 OnClick 영구 리스너를 연결해 두므로, 없을 때만 코드로 연결한다.
        private void OnEnable()
        {
            BindIfEmpty(signupButton, OnSignupClicked);
            BindIfEmpty(loginButton, OnLoginClicked);
            BindIfEmpty(logoutButton, OnLogoutClicked);
            BindIfEmpty(createRoomButton, OnCreateRoomClicked);
            BindIfEmpty(joinRoomButton, OnJoinRoomClicked);
            BindIfEmpty(leaveRoomButton, OnLeaveRoomClicked);
            BindIfEmpty(playersButton, OnPlayersClicked);
            BindIfEmpty(refreshRoomsButton, OnRefreshRoomsClicked);
        }

        private void OnDisable()
        {
            Unbind(signupButton, OnSignupClicked);
            Unbind(loginButton, OnLoginClicked);
            Unbind(logoutButton, OnLogoutClicked);
            Unbind(createRoomButton, OnCreateRoomClicked);
            Unbind(joinRoomButton, OnJoinRoomClicked);
            Unbind(leaveRoomButton, OnLeaveRoomClicked);
            Unbind(playersButton, OnPlayersClicked);
            Unbind(refreshRoomsButton, OnRefreshRoomsClicked);
        }

        private void Start()
        {
            RefreshLoginState();
            ShowCurrentRoom();
            AppendLog($"서버 주소: {baseUrl}");
        }

        private static void BindIfEmpty(Button button, UnityEngine.Events.UnityAction action)
        {
            if (button != null && button.onClick.GetPersistentEventCount() == 0)
                button.onClick.AddListener(action);
        }

        private static void Unbind(Button button, UnityEngine.Events.UnityAction action)
        {
            if (button != null) button.onClick.RemoveListener(action);
        }

        // ---------- 버튼 핸들러 (에디터 툴이 OnClick에 연결) ----------

        public void OnSignupClicked()
        {
            if (isBusy) return;

            string username = usernameInput.text.Trim();
            string password = passwordInput.text;
            string nickname = nicknameInput.text.Trim();

            if (!AuthValidator.Validate(username, password, out string error))
            {
                AppendLog($"[입력 오류] {error}", FailColor);
                return;
            }
            if (nickname.Length < 2 || nickname.Length > 10)
            {
                AppendLog("[입력 오류] 닉네임은 2~10자여야 합니다.", FailColor);
                return;
            }

            var body = new SignupRequest { username = username, password = password, nickname = nickname };
            StartCoroutine(Run(Send(UnityWebRequest.kHttpVerbPOST, SignupPath, body, false, "회원가입", null)));
        }

        public void OnLoginClicked()
        {
            if (isBusy) return;

            string username = usernameInput.text.Trim();
            string password = passwordInput.text;
            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
            {
                AppendLog("[입력 오류] 아이디와 비밀번호를 입력하세요.", FailColor);
                return;
            }

            var body = new LoginRequest { username = username, password = password };
            StartCoroutine(Run(Send(UnityWebRequest.kHttpVerbPOST, LoginPath, body, false, "로그인", OnLoginResult)));
        }

        public void OnLogoutClicked()
        {
            if (isBusy) return;
            AuthSession.Clear();
            currentRoomId = 0;
            RefreshLoginState();
            ShowCurrentRoom();
            AppendLog("로그아웃했습니다.", OkColor);
        }

        public void OnRefreshRoomsClicked()
        {
            if (isBusy) return;
            StartCoroutine(Run(Send(UnityWebRequest.kHttpVerbGET, RoomsPath, null, true, "방 목록", OnRoomListResult)));
        }

        public void OnCreateRoomClicked()
        {
            if (isBusy) return;

            string title = roomTitleInput.text.Trim();
            if (string.IsNullOrEmpty(title))
            {
                AppendLog("[입력 오류] 방 제목을 입력하세요.", FailColor);
                return;
            }
            if (!int.TryParse(maxPlayersInput != null ? maxPlayersInput.text : null, out int maxPlayers) || maxPlayers < 2)
                maxPlayers = DefaultMaxPlayers;

            var body = new RoomCreateRequest { title = title, maxPlayers = maxPlayers };
            StartCoroutine(Run(Send(UnityWebRequest.kHttpVerbPOST, RoomsPath, body, true, "방 만들기", OnCreateRoomResult)));
        }

        public void OnJoinRoomClicked()
        {
            if (isBusy || !TryGetRoomId(out long roomId)) return;
            StartCoroutine(Run(Send(UnityWebRequest.kHttpVerbPOST, $"{RoomsPath}/{roomId}/players", null, true,
                $"방 #{roomId} 입장", res => OnJoinResult(roomId, res))));
        }

        public void OnLeaveRoomClicked()
        {
            if (isBusy || !TryGetRoomId(out long roomId)) return;
            StartCoroutine(Run(Send(UnityWebRequest.kHttpVerbDELETE, $"{RoomsPath}/{roomId}/players/me", null, true,
                $"방 #{roomId} 퇴장", res => OnLeaveResult(roomId))));
        }

        public void OnPlayersClicked()
        {
            if (isBusy || !TryGetRoomId(out long roomId)) return;
            StartCoroutine(Run(Send(UnityWebRequest.kHttpVerbGET, $"{RoomsPath}/{roomId}/players", null, true,
                $"방 #{roomId} 참가자", OnPlayersResult)));
        }

        // ---------- 응답 처리 ----------

        private void OnLoginResult(ApiResult result)
        {
            AuthResponse res = SafeParse<AuthResponse>(result.body);
            if (res == null || string.IsNullOrEmpty(res.accessToken))
            {
                AppendLog("[오류] 로그인 응답에 accessToken이 없습니다.", FailColor);
                return;
            }

            AuthSession.SetSession(res.memberId, res.username, res.accessToken);
            AppendLog($"로그인 성공: {res.username} (memberId={res.memberId}, token={AuthSession.TokenPreview()})", OkColor);
            RefreshLoginState();
            // 방 목록 갱신은 현재 요청이 끝난 뒤(isBusy 해제 후) 새 요청으로 보낸다.
            StartCoroutine(RunAfterCurrent(OnRefreshRoomsClicked));
        }

        private void OnRoomListResult(ApiResult result)
        {
            RoomListResponse list = SafeParse<RoomListResponse>("{\"items\":" + result.body + "}");
            if (list == null || list.items == null)
            {
                AppendLog("[오류] 방 목록을 해석하지 못했습니다.", FailColor);
                return;
            }

            var sb = new StringBuilder();
            sb.AppendLine($"[방 목록] {list.items.Count}개");
            foreach (RoomInfoResponse r in list.items)
                sb.AppendLine($"#{r.id}  {r.title}  ({r.currentPlayers}/{r.maxPlayers})  방장 userId={r.hostUserId}");
            roomListText.text = sb.ToString();
        }

        private void OnCreateRoomResult(ApiResult result)
        {
            RoomInfoResponse room = SafeParse<RoomInfoResponse>(result.body);
            if (room != null)
            {
                currentRoomId = room.id;
                if (roomIdInput != null) roomIdInput.text = room.id.ToString();
                ShowCurrentRoom();
                AppendLog($"방 생성됨: #{room.id} {room.title} ({room.currentPlayers}/{room.maxPlayers})", OkColor);
            }
            StartCoroutine(RunAfterCurrent(OnRefreshRoomsClicked));
        }

        private void OnJoinResult(long roomId, ApiResult result)
        {
            currentRoomId = roomId;
            ShowCurrentRoom();
            AppendLog($"방 #{roomId} 입장 완료", OkColor);
            StartCoroutine(RunAfterCurrent(OnRefreshRoomsClicked, OnPlayersClicked));
        }

        private void OnLeaveResult(long roomId)
        {
            if (currentRoomId == roomId) currentRoomId = 0;
            ShowCurrentRoom();
            playersText.text = "[참가자] (방에 없음)";
            AppendLog($"방 #{roomId} 퇴장 완료", OkColor);
            StartCoroutine(RunAfterCurrent(OnRefreshRoomsClicked));
        }

        private void OnPlayersResult(ApiResult result)
        {
            RoomPlayerListResponse list = SafeParse<RoomPlayerListResponse>("{\"items\":" + result.body + "}");
            if (list == null || list.items == null)
            {
                AppendLog("[오류] 참가자 목록을 해석하지 못했습니다.", FailColor);
                return;
            }

            var sb = new StringBuilder();
            sb.AppendLine($"[참가자] {list.items.Count}명");
            foreach (RoomPlayerResponse p in list.items)
                sb.AppendLine($"{p.nickname}  (userId={p.userId})  {(p.ready ? "READY" : "-")}");
            playersText.text = sb.ToString();
        }

        // ---------- 공통 ----------

        // 입력칸의 방 번호를 읽고, 비어 있으면 현재 방을 사용한다.
        private bool TryGetRoomId(out long roomId)
        {
            roomId = 0;
            string text = roomIdInput != null ? roomIdInput.text.Trim() : "";
            if (string.IsNullOrEmpty(text) && currentRoomId > 0)
            {
                roomId = currentRoomId;
                return true;
            }
            if (long.TryParse(text, out roomId) && roomId > 0) return true;

            AppendLog("[입력 오류] 방 번호(Room ID)를 숫자로 입력하세요.", FailColor);
            return false;
        }

        private void ShowCurrentRoom()
        {
            if (currentRoomText != null)
                currentRoomText.text = currentRoomId > 0 ? $"현재 방: #{currentRoomId}" : "현재 방: 없음";
        }

        private void RefreshLoginState()
        {
            bool loggedIn = AuthSession.IsAuthenticated;
            if (lobbyPanel != null) lobbyPanel.SetActive(loggedIn);
            if (logoutButton != null) logoutButton.gameObject.SetActive(loggedIn);
            if (authStatusText != null)
                authStatusText.text = loggedIn
                    ? $"로그인됨: {AuthSession.Username}\n토큰: {AuthSession.TokenPreview()}"
                    : "로그인 필요";
            if (!loggedIn)
            {
                if (roomListText != null) roomListText.text = "[방 목록] (로그인 후 표시)";
                if (playersText != null) playersText.text = "[참가자] (방을 선택하세요)";
            }
            else
            {
                if (roomListText != null) roomListText.text = "[방 목록] Refresh Rooms를 누르세요";
                if (playersText != null) playersText.text = "[참가자] (방을 선택하세요)";
            }
        }

        // 요청 하나를 실행하는 동안 중복 클릭을 막는다.
        private IEnumerator Run(IEnumerator request)
        {
            isBusy = true;
            SetButtonsInteractable(false);
            yield return request;
            isBusy = false;
            SetButtonsInteractable(true);
        }

        // 진행 중인 요청이 끝나기를 기다렸다가 액션을 순서대로 실행한다. (각 액션은 요청 하나를 시작한다)
        private IEnumerator RunAfterCurrent(params Action[] actions)
        {
            foreach (Action action in actions)
            {
                while (isBusy) yield return null;
                action();
            }
        }

        private void SetButtonsInteractable(bool value)
        {
            SetInteractable(signupButton, value);
            SetInteractable(loginButton, value);
            SetInteractable(logoutButton, value);
            SetInteractable(createRoomButton, value);
            SetInteractable(joinRoomButton, value);
            SetInteractable(leaveRoomButton, value);
            SetInteractable(playersButton, value);
            SetInteractable(refreshRoomsButton, value);
        }

        private static void SetInteractable(Button b, bool value)
        {
            if (b != null) b.interactable = value;
        }

        private IEnumerator Send(string method, string path, object body, bool needsAuth, string label, Action<ApiResult> onSuccess)
        {
            if (needsAuth && !AuthSession.IsAuthenticated)
            {
                AppendLog($"[{label}] 로그인이 필요합니다.", FailColor);
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

                AppendLog($"→ {method} {path}");
                yield return req.SendWebRequest();

                ApiResult result = BuildResult(req);
                AppendLog($"← [{label}] {(result.success ? "성공" : "실패")} {result.message}", result.success ? OkColor : FailColor);
                if (!string.IsNullOrEmpty(result.body)) AppendLog(result.body);

                // 토큰이 만료/무효면 세션을 비우고 로그인 전 상태로 되돌린다.
                if (!result.success && result.responseCode == 401 && needsAuth)
                {
                    AuthSession.Clear();
                    currentRoomId = 0;
                    RefreshLoginState();
                    ShowCurrentRoom();
                    AppendLog("토큰이 유효하지 않아 로그아웃되었습니다. 다시 로그인하세요.", FailColor);
                }

                if (result.success) onSuccess?.Invoke(result);
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
                    ErrorResponse err = SafeParse<ErrorResponse>(result.body);
                    string detail = err != null && !string.IsNullOrEmpty(err.message) ? err.message
                                  : err != null && !string.IsNullOrEmpty(err.error) ? err.error
                                  : req.error;
                    result.message = $"HTTP {req.responseCode} {detail}";
                    break;

                default: // ConnectionError, DataProcessingError
                    result.message = $"서버에 연결할 수 없습니다. ({req.error})";
                    break;
            }
            return result;
        }

        private static T SafeParse<T>(string json) where T : class
        {
            if (string.IsNullOrEmpty(json)) return null;
            try { return JsonUtility.FromJson<T>(json); }
            catch (Exception) { return null; }
        }

        private void AppendLog(string line, string color = null)
        {
            string stamped = $"[{DateTime.Now:HH:mm:ss}] {line}";
            log.AppendLine(color == null ? stamped : $"<color={color}>{stamped}</color>");
            Debug.Log("[LobbyTest] " + line);

            // 오래된 로그는 줄 단위로 잘라 색상 태그가 깨지지 않게 한다.
            while (log.Length > MaxLogChars)
            {
                int cut = -1;
                for (int i = 0; i < log.Length; i++)
                {
                    if (log[i] == '\n') { cut = i + 1; break; }
                }
                if (cut <= 0) break;
                log.Remove(0, cut);
            }

            if (logText == null) return;
            logText.text = log.ToString();
            if (logScroll != null)
            {
                Canvas.ForceUpdateCanvases();
                logScroll.verticalNormalizedPosition = 0f;
            }
        }
    }
}
