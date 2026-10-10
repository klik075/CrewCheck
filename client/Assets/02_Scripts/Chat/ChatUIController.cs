using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using TMPro;
using WhoisntCitizen.Lobby; // RoomSession: 로비에서 입장한 방
using UnityEngine;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
#endif

namespace WhoisntCitizen.Chat
{
    /// <summary>
    /// 채팅 UI 컨트롤러 (ChatPrac ChatScene을 WhoisntCitizen_server API에 맞게 옮김)
    ///  - 최신 N개(기본 10개) 메시지 표시, 가로 초과 시 자동 줄바꿈
    ///  - Enter: 전송 / Shift+Enter: 줄바꿈 (한글 IME 조합이 끝난 뒤 전송)
    ///  - pollInterval초마다 afterId 이후 새 메시지만 받아 뒤에 붙이고, N회마다 전체 목록을 다시 받습니다
    ///  - 시스템 메시지(입장·퇴장 알림, 공지)는 녹색으로 표시하고, Unity 콘솔에도 같은 내용을 출력
    ///  - SendSystemMessage(): 시스템 메시지(공지) 전송 (에디터 창 Tools > Chat > System Message Console에서 사용)
    ///  - 내 화면 전용 안내(ChatNotice: 로그인, 방 입장·퇴장, 입력 오류, 연결 상태 등)는 [안내] 말머리로 표시하고 콘솔에도 출력
    ///  - 내 메시지 구분은 userId로 (개발용 로그인 응답 또는 내가 보낸 메시지의 응답에서 알아냄)
    /// 시작 순서: 로그인(AuthSession에 토큰이 없을 때만, 개발용) → 로비 방 참가(개발용) → 메시지 조회/폴링
    /// 로비에서 방에 들어온 상태(RoomSession.HasRoom)면 그 방으로 바로 채팅을 시작합니다. (로그인·입장 생략)
    /// 로비 화면에서 로그인·방 입장을 마치고 들어오면 [개발용] 옵션은 꺼도 됩니다.
    /// </summary>
    public class ChatUIController : MonoBehaviour, IChatSystemSender
    {
        [Header("References")]
        public ChatApiClient api;
        public TMP_InputField inputField;
        public Button sendButton;
        public TMP_Text messagesText;
        public ScrollRect messagesScroll;
        public LayoutElement inputBoxLayout;

        [Header("Chat")]
        public int maxMessages = 10;
        [Tooltip("내 userId (프로필 id). -1이면 개발용 로그인 응답이나 첫 전송 응답으로 자동 설정. 로비에서 넘겨받으면 여기에 넣어 주세요.")]
        public long myUserId = -1;

        [Header("[개발용] 단독 실행 (로비 없이 이 씬만 플레이할 때)")]
        [Tooltip("AuthSession에 토큰이 없으면 아래 계정으로 로그인 (Postman 0번 폴더의 테스트 계정). 에디터에서만 동작한다.")]
        public bool autoLogin = true;
        public string devUsername = "tester1";
        public string devPassword = "Test1234!";
        [Tooltip("시작할 때 로비 방에 참가 (POST /api/v1/rooms/{roomId}/players). 이미 참가 중(409)이면 그대로 진행")]
        public bool autoJoinRoom = true;
        [Tooltip("방이 없으면(또는 roomId가 0 이하면) 새 방을 만들어 그 방으로 채팅")]
        public bool createRoomIfMissing = true;
        public string devRoomTitle = "채팅 테스트방";
        public int devRoomMaxPlayers = 8;

        [Header("Polling (새 메시지 자동 수신)")]
        [Tooltip("새 메시지를 확인하는 간격(초). 0 이하면 자동 수신 끔")]
        public float pollInterval = 2f;
        [Tooltip("폴링 N회마다 한 번은 전체 목록을 다시 받아 동기화 (서버/Redis 초기화 대비)")]
        public int fullSyncEveryPolls = 15;
        [Tooltip("서버에 연결하지 못했을 때 로그인/입장을 다시 시도하는 간격(초)")]
        public float retryInterval = 3f;

        [Header("Input Box")]
        [Tooltip("입력 박스 기본 높이 (54pt x 1.5)")]
        public float baseInputHeight = 81f;
        [Tooltip("Shift+Enter로 줄이 늘어날 때 입력 박스가 커지는 최대 줄 수 (넘으면 박스 안에서 스크롤)")]
        public int maxVisibleInputLines = 4;

        [Header("Style")]
        public Color myNameColor = new Color(0.45f, 0.8f, 1f);
        public Color otherNameColor = new Color(1f, 0.85f, 0.4f);
        public Color errorColor = new Color(1f, 0.45f, 0.45f);
        [Tooltip("시스템 메시지(입장 알림, 공지) 색")]
        public Color systemColor = new Color(0.4f, 0.9f, 0.45f);
        [Tooltip("시스템 메시지 앞에 붙는 말머리")]
        public string systemPrefix = "[시스템] ";

        [Tooltip("내 화면 전용 안내(ChatNotice) 색")]
        public Color localNoticeColor = new Color(0.62f, 0.91f, 0.63f);
        [Tooltip("내 화면 전용 안내 앞에 붙는 말머리")]
        public string localNoticePrefix = "[안내] ";
        [Tooltip("사망자 채팅(type=DEAD) 글자색. 닉네임과 내용 모두 이 색으로 표시합니다")]
        public Color deadColor = new Color(0.6f, 0.6f, 0.6f);
        [Tooltip("밤에 해적이 입력한 채팅(nightChat)의 내용 글자색. 닉네임은 일반 채팅과 같은 색(내 것/남의 것)을 씁니다")]
        public Color nightChatColor = new Color(1f, 0.6f, 0.2f);

        [Header("Console (Unity 콘솔 연동)")]
        [Tooltip("채팅창에 새로 표시되는 시스템 메시지를 Unity 콘솔에도 출력")]
        public bool logSystemMessagesToConsole = true;

        readonly List<ChatMessage> _messages = new List<ChatMessage>();
        string _status = "";       // 일시적인 오류 (다음 조회가 성공하면 지움)
        string _fatal = "";        // 다시 시도해도 안 되는 오류 (로그인 실패, 방 없음 등, 화면에 계속 표시)
        int _sendQueuedFrame = -1;
        bool _sending;
        bool _fetching;
        bool _fetchAgain;
        bool _fullSyncRequested;
        bool _stickToBottom = true;
        bool _ready;               // 로그인 + 방 참가가 끝나 채팅할 수 있는 상태
        bool _connecting;
        long _lastId = -1;         // 마지막으로 받은 메시지 id (-1: 아직 없음 → 전체 조회)
        long _lastLoggedSystemId = -1; // 콘솔에 마지막으로 출력한 시스템 메시지 id (중복 출력 방지)
        bool _sendingSystem;
        bool _joinBlockedByGame;   // 입장 시 "게임이 진행 중인 방" 409를 받음 (참가자가 아니면 전송 시 403)

        /// 내 화면 전용 안내. anchor = 이 안내가 생겼을 때 마지막으로 받은 메시지 id (그 메시지 뒤에 표시)
        class LocalNotice
        {
            public long anchor;
            public string text;
        }
        const long UnresolvedAnchor = long.MinValue; // 첫 조회 전에 생긴 안내 → 첫 목록을 받은 뒤 맨 뒤에 붙임
        readonly List<LocalNotice> _notices = new List<LocalNotice>();
        float _nextPollTime;
        int _pollCount;
        bool _imeComposing;
#if ENABLE_INPUT_SYSTEM
        Keyboard _imeKeyboard;
#endif

        static readonly Regex NoParseCloseTag = new Regex("</\\s*noparse\\s*>", RegexOptions.IgnoreCase);

        void Awake()
        {
            if (api == null) api = GetComponent<ChatApiClient>();

            inputField.lineType = TMP_InputField.LineType.MultiLineNewline;
            inputField.textComponent.textWrappingMode = TextWrappingModes.Normal;
            inputField.onValidateInput = ValidateInput;
            inputField.onSelect.AddListener(_ => EnableIme(true));
            inputField.onDeselect.AddListener(_ => EnableIme(false));
            sendButton.onClick.AddListener(Send);

            messagesText.textWrappingMode = TextWrappingModes.Normal;
            messagesText.richText = true;
        }

        void OnEnable()
        {
            SubscribeIme();
            ChatNotice.Posted += OnChatNotice;
        }

        void OnDisable()
        {
            ChatNotice.Posted -= OnChatNotice;
#if ENABLE_INPUT_SYSTEM
            if (_imeKeyboard != null) _imeKeyboard.onIMECompositionChange -= OnImeComposition;
            _imeKeyboard = null;
#endif
            _imeComposing = false;
        }

        void Start()
        {
            // 로비 등 채팅창이 없던 씬에서 쌓인 안내 (콘솔에는 그때 이미 출력됨)
            foreach (var text in ChatNotice.TakePending()) AddNotice(text);
            Render();
            StartCoroutine(Connect());
            inputField.ActivateInputField();
        }

        void Update()
        {
            SubscribeIme(); // 키보드가 나중에 연결된 경우 대비
            UpdateInputHeight();

            if (!_ready || pollInterval <= 0f || Time.unscaledTime < _nextPollTime) return;
            _nextPollTime = Time.unscaledTime + pollInterval;
            if (_fetching) return; // 이미 조회 중이면 이번 폴링은 건너뜀

            _pollCount++;
            bool fullSync = fullSyncEveryPolls > 0 && _pollCount % fullSyncEveryPolls == 0;
            RequestFetch(fullSync);
        }

        void LateUpdate()
        {
            // Enter 입력 후 최소 한 프레임 뒤, 한글 IME 조합이 확정된 뒤에 전송
            if (_sendQueuedFrame >= 0 && Time.frameCount > _sendQueuedFrame && !IsImeComposing())
            {
                _sendQueuedFrame = -1;
                Send();
            }
        }

        // ---------------- 연결 (로그인 → 방 참가) ----------------

        IEnumerator Connect()
        {
            if (_connecting) yield break;
            _connecting = true;
            _ready = false;

            while (true)
            {
                string err = null;
                long code = 0;

                if (AuthSession.IsAuthenticated && myUserId < 0 && AuthSession.UserId > 0)
                    myUserId = AuthSession.UserId; // 타이틀/로비에서 로그인한 경우

                // 1) 로그인 (로비에서 이미 로그인했다면 건너뜀)
                if (!AuthSession.IsAuthenticated)
                {
                    if (!autoLogin || !Application.isEditor) // 빌드에서는 개발용 계정으로 로그인하지 않는다
                    {
                        Fail("로그인 후 이용할 수 있습니다.", "AuthSession에 토큰이 없고 [개발용] autoLogin이 꺼져 있거나 빌드임");
                        break;
                    }
                    yield return api.Login(devUsername, devPassword,
                        r =>
                        {
                            AuthSession.SetSession(r.memberId, r.userId, devUsername, r.nickname, r.accessToken);
                            myUserId = r.userId;
                            ChatNotice.Post((string.IsNullOrEmpty(r.nickname) ? devUsername : r.nickname) + "님, 환영합니다.");
                        },
                        (e, c) => { err = e; code = c; });
                    if (err != null)
                    {
                        if (code >= 400 && code < 500) { Fail(err); break; } // 계정 정보 오류는 재시도해도 같음
                        SetError(Friendly(err, code), err);
                        yield return new WaitForSecondsRealtime(retryInterval);
                        continue;
                    }
                }

                // 2) 로비 방 참가 (로비에서 이미 입장한 방이 있으면 그 방을 그대로 사용)
                if (RoomSession.HasRoom)
                {
                    api.roomId = RoomSession.RoomId;
                }
                else if (autoJoinRoom)
                {
                    bool needCreate = api.roomId <= 0 && createRoomIfMissing;
                    if (!needCreate)
                    {
                        err = null; code = 0;
                        yield return api.JoinRoom(null, (e, c) => { err = e; code = c; });
                        if (err != null)
                        {
                            if (code == 401) { NotifyExpired(); continue; } // 토큰 만료 → 다시 로그인
                            if (code == 409)
                            {
                                // 409 사유는 서버 메시지로 구분 (RoomService.joinRoom)
                                if (err.Contains("가득"))
                                {
                                    Fail("방이 가득 차 입장할 수 없습니다.", err);
                                    break;
                                }
                                if (err.Contains("게임이 진행 중"))
                                {
                                    // 이미 참가 중인 플레이어도 게임 중에는 이 409를 받으므로 일단 진행하고,
                                    // 참가자가 아니면 전송할 때(403) 안내한다.
                                    _joinBlockedByGame = true;
                                    Debug.Log("[Chat] " + err);
                                }
                                else
                                {
                                    ChatNotice.Post("이미 참가 중인 방입니다. 채팅을 이어서 진행합니다.");
                                }
                            }
                            else if ((code == 400 || code == 404) && createRoomIfMissing)
                            {
                                Debug.LogWarning("[Chat] " + err);
                                ChatNotice.Post("방을 찾을 수 없어 새 방을 만듭니다.");
                                needCreate = true;
                            }
                            else if (code >= 400 && code < 500) { Fail(err); break; }
                            else
                            {
                                SetError(Friendly(err, code), err);
                                yield return new WaitForSecondsRealtime(retryInterval);
                                continue;
                            }
                        }
                    }

                    if (needCreate)
                    {
                        err = null; code = 0;
                        long newId = -1;
                        yield return api.CreateRoom(devRoomTitle, devRoomMaxPlayers, id => newId = id, (e, c) => { err = e; code = c; });
                        if (err != null)
                        {
                            if (code == 401) { NotifyExpired(); continue; }
                            if (code >= 400 && code < 500) { Fail(err); break; }
                            SetError(Friendly(err, code), err);
                            yield return new WaitForSecondsRealtime(retryInterval);
                            continue;
                        }
                        api.roomId = newId;
                        _joinBlockedByGame = false;
                        Debug.Log("[Chat] 새 방을 만들었습니다. roomId=" + newId);
                        ChatNotice.Post("참가할 방이 없어 '" + devRoomTitle + "' 방을 새로 만들었습니다.");
                    }
                }

                // 3) 채팅 시작
                _ready = true;
                _fatal = "";
                ClearStatus();
                _nextPollTime = Time.unscaledTime + pollInterval;
                RequestFetch(true);
                break;
            }

            _connecting = false;
        }

        /// <summary>토큰이 만료되는 등 401을 받으면 다시 연결합니다.</summary>
        void HandleUnauthorized()
        {
            _ready = false;
            NotifyExpired();
            StartCoroutine(Connect());
        }

        /// 로그인 만료 안내 + 세션 비우기 (autoLogin이 켜져 있으면 Connect가 다시 로그인)
        void NotifyExpired()
        {
            AuthSession.Clear();
            ChatNotice.Post("로그인 시간이 만료되었습니다. 다시 로그인해 주세요.");
        }

        // ---------------- 입력 처리 ----------------

        char ValidateInput(string text, int charIndex, char addedChar)
        {
            if (addedChar == '\n' || addedChar == '\r')
            {
                if (IsShiftHeld()) return '\n'; // Shift+Enter → 줄바꿈
                _sendQueuedFrame = Time.frameCount; // Enter → 전송 (IME 조합 확정 후 LateUpdate에서 처리)
                return '\0';
            }
            return addedChar;
        }

        static bool IsShiftHeld()
        {
#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            if (kb != null && kb.shiftKey.isPressed) return true;
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)) return true;
#endif
            return false;
        }

        bool IsImeComposing()
        {
            // 이 프로젝트는 Active Input Handling = Input System 이므로 키보드의 IME 조합 이벤트로 판단합니다.
            if (_imeComposing) return true;
#if ENABLE_LEGACY_INPUT_MANAGER
            if (!string.IsNullOrEmpty(Input.compositionString)) return true;
#endif
            return false;
        }

        void SubscribeIme()
        {
#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            if (kb == _imeKeyboard) return;
            if (_imeKeyboard != null) _imeKeyboard.onIMECompositionChange -= OnImeComposition;
            _imeKeyboard = kb;
            _imeComposing = false;
            if (kb != null) kb.onIMECompositionChange += OnImeComposition;
#endif
        }

#if ENABLE_INPUT_SYSTEM
        void OnImeComposition(IMECompositionString composition)
        {
            _imeComposing = composition.Count > 0;
        }
#endif

        static void EnableIme(bool on)
        {
            // 입력 박스가 활성화되면 한글 IME 조합 입력을 켭니다
#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            if (kb != null) kb.SetIMEEnabled(on);
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            Input.imeCompositionMode = on ? IMECompositionMode.On : IMECompositionMode.Auto;
#endif
        }

        public void Send()
        {
            string text = inputField.text.Trim();
            if (string.IsNullOrEmpty(text) || _sending) { FocusInput(); return; }
            if (!_ready)
            {
                SetError(string.IsNullOrEmpty(_fatal) ? "서버와 연결이 끊어졌습니다. 다시 연결하는 중입니다..." : _fatal);
                FocusInput();
                return;
            }

            _sending = true;
            sendButton.interactable = false;
            inputField.text = "";
            FocusInput();

            StartCoroutine(api.PostMessage(text,
                senderId =>
                {
                    if (senderId >= 0 && senderId != myUserId) { myUserId = senderId; Render(); }
                    _sending = false;
                    sendButton.interactable = true;
                    _status = "";
                    _stickToBottom = true; // 내가 보낸 메시지는 항상 보이도록
                    RequestFetch();
                },
                (err, code) =>
                {
                    _sending = false;
                    sendButton.interactable = true;
                    inputField.text = text; // 실패 시 입력 내용 복구
                    inputField.caretPosition = text.Length;
                    SetError(Friendly(err, code), err);
                    if (code == 401) HandleUnauthorized();
                }));
        }

        void FocusInput()
        {
            inputField.ActivateInputField();
            inputField.Select();
        }

        void UpdateInputHeight()
        {
            if (inputBoxLayout == null) return;
            var tc = inputField.textComponent;
            int lines = Mathf.Max(1, tc.textInfo != null ? tc.textInfo.lineCount : 1);
            float lineH = tc.font != null && tc.font.faceInfo.pointSize > 0
                ? tc.font.faceInfo.lineHeight / tc.font.faceInfo.pointSize * tc.fontSize
                : tc.fontSize * 1.2f;
            float target = baseInputHeight + (Mathf.Min(lines, maxVisibleInputLines) - 1) * lineH;
            if (!Mathf.Approximately(inputBoxLayout.preferredHeight, target))
                inputBoxLayout.preferredHeight = target;
        }

        // ---------------- 조회/표시 ----------------

        /// <param name="fullSync">true면 최신 목록 전체를 다시 받고, false면 마지막 id 이후 새 메시지만 받습니다</param>
        public void RequestFetch(bool fullSync = false)
        {
            if (!_ready) return;
            if (fullSync) _fullSyncRequested = true;
            if (_fetching) { _fetchAgain = true; return; }
            StartCoroutine(FetchRoutine());
        }

        IEnumerator FetchRoutine()
        {
            _fetching = true;
            do
            {
                _fetchAgain = false;
                bool full = _fullSyncRequested || _lastId < 0;
                _fullSyncRequested = false;

                long errCode = 0;
                if (full)
                    yield return api.FetchMessages(-1, maxMessages, OnFullListReceived, (e, c) => { errCode = c; SetError(Friendly(e, c), e); });
                else
                    yield return api.FetchMessages(_lastId, 0, OnNewMessagesReceived, (e, c) => { errCode = c; SetError(Friendly(e, c), e); });

                if (errCode == 401) { HandleUnauthorized(); break; }
            } while (_fetchAgain && _ready);
            _fetching = false;
        }

        /// 전체 목록으로 교체 (시작 시 / 주기적 동기화)
        public event System.Action<ChatMessage> LiveMessageReceived;
        private bool hasLiveSnapshot;

        void OnFullListReceived(List<ChatMessage> list)
        {
            long previousId = _lastId;
            bool notifyLive = hasLiveSnapshot;
            hasLiveSnapshot = true;
            bool statusChanged = ClearStatus();
            var sorted = SortOldestFirst(list).ToList();
            if (!statusChanged && SameIds(sorted, _messages)) return; // 바뀐 게 없으면 다시 그리지 않음

            long newMax = MaxId(sorted);
            if (newMax < _lastLoggedSystemId) _lastLoggedSystemId = -1; // 서버/Redis 초기화로 id가 처음부터 다시 매겨진 경우

            _messages.Clear();
            _messages.AddRange(sorted);
            TrimToMax();
            _lastId = MaxId(_messages);
            if (notifyLive)
                foreach (var message in sorted)
                    if (long.TryParse(message.id, out long id) && id > previousId)
                        LiveMessageReceived?.Invoke(message);
            ResolveNoticeAnchors();
            LogNewSystemMessages();
            Render();
        }

        /// afterId 이후 새 메시지만 목록 뒤에 추가 (폴링 / 전송 직후)
        void OnNewMessagesReceived(List<ChatMessage> list)
        {
            bool statusChanged = ClearStatus();
            bool added = false;
            foreach (var m in SortOldestFirst(list))
            {
                long id;
                bool hasId = long.TryParse(m.id, out id);
                if (hasId && id <= _lastId) continue; // 이미 받은 메시지
                _messages.Add(m);
                if (hasId) LiveMessageReceived?.Invoke(m);
                if (hasId && id > _lastId) _lastId = id;
                added = true;
            }
            if (!added && !statusChanged) return;
            TrimToMax();
            ResolveNoticeAnchors();
            LogNewSystemMessages();
            Render();
        }

        // ---------------- 내 화면 전용 안내 (ChatNotice) ----------------

        void OnChatNotice(string text)
        {
            AddNotice(text);
            _stickToBottom = true;
            Render();
        }

        /// 안내를 지금까지 받은 메시지 뒤에 붙입니다. (서버에 저장되지 않으므로 다른 사람에게는 보이지 않음)
        void AddNotice(string text)
        {
            _notices.Add(new LocalNotice { anchor = _lastId >= 0 ? _lastId : UnresolvedAnchor, text = text });
            if (_notices.Count > maxMessages) _notices.RemoveRange(0, _notices.Count - maxMessages);
        }

        /// 첫 목록을 받기 전에 생긴 안내는 받은 목록의 맨 뒤에 오도록 고정합니다.
        void ResolveNoticeAnchors()
        {
            foreach (var n in _notices)
                if (n.anchor == UnresolvedAnchor) n.anchor = _lastId;
        }

        /// 오류를 채팅창에 보여 줄 문장으로 바꿉니다. (원문은 콘솔에 함께 남김)
        string Friendly(string err, long code)
        {
            if (code == 0) return "서버와 연결이 끊어졌습니다. 다시 연결하는 중입니다...";
            if (code == 401) return "로그인 시간이 만료되었습니다. 다시 로그인해 주세요.";
            if (code == 403 && err != null && err.StartsWith("전송 실패"))
            {
                if (_joinBlockedByGame) return "게임이 진행 중이라 지금은 입장할 수 없습니다.";
                // 서버 안내 문장을 그대로 보여 준다 (예: "밤에는 해적만 채팅할 수 있습니다.")
                int i = err.IndexOf("): ", System.StringComparison.Ordinal);
                return i >= 0 ? err.Substring(i + 3) : "지금은 채팅에 참여할 수 없습니다.";
            }
            return err;
        }

        /// 채팅창에 새로 들어온 시스템 메시지를 Unity 콘솔에도 출력 (이미 출력한 id는 건너뜀)
        void LogNewSystemMessages()
        {
            foreach (var m in _messages)
            {
                if (!m.IsSystem) continue;
                long id;
                if (!long.TryParse(m.id, out id) || id <= _lastLoggedSystemId) continue;
                _lastLoggedSystemId = id;
                if (logSystemMessagesToConsole)
                    Debug.Log("<color=#" + ColorUtility.ToHtmlStringRGB(systemColor) + ">[Chat][시스템]</color> " + m.content
                              + "  (room " + api.roomId + ", #" + id + ")");
            }
        }

        // ---------------- 시스템 메시지(공지) 전송 ----------------

        /// <summary>
        /// 시스템 메시지(공지)를 서버에 보냅니다. 채팅창에는 녹색 [시스템] 메시지로 표시되고, 콘솔에도 출력됩니다.
        /// 에디터 창(Tools > Chat > System Message Console)이나 다른 스크립트(게임 페이즈 알림 등)에서 호출합니다.
        /// </summary>
        /// <param name="onDone">(성공 여부, 오류 문구)</param>
        public void SendSystemMessage(string text, System.Action<bool, string> onDone = null)
        {
            text = text == null ? "" : text.Trim();
            string reason = null;
            if (text.Length == 0) reason = "보낼 내용이 없습니다.";
            else if (_sendingSystem) reason = "이전 공지를 보내는 중입니다.";
            else if (!_ready || !AuthSession.IsAuthenticated) reason = "서버에 연결된 뒤 공지를 보낼 수 있습니다.";
            if (reason != null)
            {
                if (_ready && AuthSession.IsAuthenticated) Debug.LogWarning("[Chat] 공지 전송 불가: " + reason);
                else ChatNotice.Post(reason); // 채팅창 + 콘솔
                if (onDone != null) onDone(false, reason);
                return;
            }

            _sendingSystem = true;
            StartCoroutine(api.PostSystemMessage(text,
                () =>
                {
                    _sendingSystem = false;
                    _stickToBottom = true;
                    RequestFetch(); // 채팅창 표시 + 콘솔 출력은 조회 결과로 처리 (다른 클라이언트와 같은 경로)
                    if (onDone != null) onDone(true, null);
                },
                (err, code) =>
                {
                    _sendingSystem = false;
                    SetError(Friendly(err, code), err);
                    if (onDone != null) onDone(false, err);
                    if (code == 401) HandleUnauthorized();
                }));
        }

        /// <summary>채팅을 주고받을 수 있는 상태인지 (에디터 창 표시용)</summary>
        public bool IsReady
        {
            get { return _ready; }
        }

        /// <summary>채팅 중인 로비 방 id (에디터 창 표시용)</summary>
        public long RoomId
        {
            get { return api != null ? api.roomId : 0; }
        }

        bool ClearStatus()
        {
            if (string.IsNullOrEmpty(_status)) return false;
            _status = "";
            return true;
        }

        void TrimToMax()
        {
            if (_messages.Count > maxMessages)
                _messages.RemoveRange(0, _messages.Count - maxMessages); // 최신 N개만 유지
        }

        static long MaxId(List<ChatMessage> list)
        {
            long max = -1, id;
            foreach (var m in list)
                if (long.TryParse(m.id, out id) && id > max) max = id;
            return max;
        }

        static bool SameIds(List<ChatMessage> a, List<ChatMessage> b)
        {
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++)
                if (a[i].id != b[i].id) return false;
            return true;
        }

        static IEnumerable<ChatMessage> SortOldestFirst(List<ChatMessage> list)
        {
            long dummy;
            if (list.Count > 0 && list.All(m => long.TryParse(m.id, out dummy)))
                return list.OrderBy(m => long.Parse(m.id));
            return list; // 정렬 기준이 없으면 서버 순서(오래된 → 최신) 그대로
        }

        /// <param name="text">채팅창에 보여 줄 문장 (빨간 글씨, 다음 조회가 성공하면 사라짐)</param>
        /// <param name="detail">콘솔에 함께 남길 원래 오류 (없으면 생략)</param>
        void SetError(string text, string detail = null)
        {
            if (text == _status) return; // 폴링 중 같은 오류가 반복되면 로그/화면 갱신 생략
            Debug.LogWarning("[Chat] " + text + (string.IsNullOrEmpty(detail) || detail == text ? "" : "  (" + detail + ")"));
            _status = text;
            Render();
        }

        /// 다시 시도해도 해결되지 않는 오류. 채팅창에 계속 표시됩니다.
        void Fail(string text, string detail = null)
        {
            Debug.LogWarning("[Chat] " + text + (string.IsNullOrEmpty(detail) || detail == text ? "" : "  (" + detail + ")"));
            _fatal = text;
            _status = "";
            Render();
        }

        void Render()
        {
            // 사용자가 위로 스크롤해서 이전 메시지를 보고 있으면 자동으로 끌어내리지 않음
            bool atBottom = messagesScroll == null
                || messagesScroll.content == null || messagesScroll.viewport == null
                || messagesScroll.content.rect.height <= messagesScroll.viewport.rect.height + 1f // 아직 스크롤할 만큼 길지 않음
                || messagesScroll.verticalNormalizedPosition <= 0.01f;
            bool scrollDown = atBottom || _stickToBottom;
            _stickToBottom = false;

            var sb = new StringBuilder();
            string myHex = ColorUtility.ToHtmlStringRGB(myNameColor);
            string otherHex = ColorUtility.ToHtmlStringRGB(otherNameColor);
            string systemHex = ColorUtility.ToHtmlStringRGB(systemColor);
            string errorHex = ColorUtility.ToHtmlStringRGB(errorColor);
            string myId = myUserId >= 0 ? myUserId.ToString(CultureInfo.InvariantCulture) : null;
            string noticeHex = ColorUtility.ToHtmlStringRGB(localNoticeColor);
            int noticeIndex = 0;

            foreach (var m in _messages)
            {
                // 이 메시지보다 먼저 생긴 내 화면 전용 안내를 앞에 끼워 넣음
                long mid;
                if (long.TryParse(m.id, out mid))
                    while (noticeIndex < _notices.Count && _notices[noticeIndex].anchor != UnresolvedAnchor && _notices[noticeIndex].anchor < mid)
                        AppendNotice(sb, _notices[noticeIndex++].text, noticeHex);

                if (sb.Length > 0) sb.Append('\n');

                if (m.IsSystem)
                {
                    // 시스템 메시지: 말머리 + 내용 전체를 녹색으로
                    sb.Append("<color=#").Append(systemHex).Append(">")
                      .Append(NoParse(systemPrefix + m.content))
                      .Append("</color>");
                    continue;
                }

                if (m.IsDead)
                {
                    // 사망자 채팅: 서버가 사망자에게만 보내 준다. 닉네임과 내용 모두 회색
                    sb.Append("<color=#").Append(ColorUtility.ToHtmlStringRGB(deadColor)).Append("><b>")
                      .Append(NoParse(string.IsNullOrEmpty(m.nickname) ? "?" : m.nickname))
                      .Append("</b>: ")
                      .Append(NoParse(m.content))
                      .Append("</color>");
                    continue;
                }

                bool mine = myId != null && m.userId == myId;
                sb.Append("<color=#").Append(mine ? myHex : otherHex).Append("><b>")
                  .Append(NoParse(string.IsNullOrEmpty(m.nickname) ? "?" : m.nickname))
                  .Append("</b></color>: ");
                // 밤에 해적이 입력한 채팅(서버가 해적에게만 보내 줌)은 내용만 주황색. 닉네임은 일반 채팅과 같은 규칙
                if (m.nightChat)
                    sb.Append("<color=#").Append(ColorUtility.ToHtmlStringRGB(nightChatColor)).Append(">")
                      .Append(NoParse(m.content))
                      .Append("</color>");
                else
                    sb.Append(NoParse(m.content));
            }

            while (noticeIndex < _notices.Count)
                AppendNotice(sb, _notices[noticeIndex++].text, noticeHex);

            AppendError(sb, _fatal, errorHex);
            if (_status != _fatal) AppendError(sb, _status, errorHex);

            messagesText.text = sb.ToString();

            // 최신 메시지가 보이도록 아래로 스크롤
            if (messagesScroll != null && scrollDown)
            {
                Canvas.ForceUpdateCanvases();
                messagesScroll.verticalNormalizedPosition = 0f;
            }
        }

        void AppendNotice(StringBuilder sb, string text, string hex)
        {
            if (sb.Length > 0) sb.Append('\n');
            sb.Append("<color=#").Append(hex).Append(">").Append(NoParse(localNoticePrefix + text)).Append("</color>");
        }

        static void AppendError(StringBuilder sb, string err, string hex)
        {
            if (string.IsNullOrEmpty(err)) return;
            if (sb.Length > 0) sb.Append('\n');
            sb.Append("<color=#").Append(hex).Append(">").Append(NoParse(err)).Append("</color>");
        }

        static string NoParse(string s)
        {
            // 사용자가 입력한 <, > 등이 리치 텍스트 태그로 해석되지 않도록 처리 (닫는 태그는 대소문자·공백 무관하게 무력화)
            return "<noparse>" + NoParseCloseTag.Replace(s ?? "", "</no parse>") + "</noparse>";
        }
    }
}
