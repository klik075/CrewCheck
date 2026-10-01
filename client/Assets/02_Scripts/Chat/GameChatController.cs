using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.UI;
using WhoisntCitizen.GameUI;
using WhoisntCitizen.Lobby; // RoomSession: 로비에서 입장한 방

namespace WhoisntCitizen.Chat
{
    /// <summary>
    /// GameScene 채팅: ChatScene(ChatUIController)의 서버 기능을 GameScene UI에 연결합니다.
    ///  - 화면: ChatLogView(채팅 기록) + BottomTabController(하단 탭의 채팅 입력/전송)
    ///  - 서버: ChatApiClient (조회·전송·공지, JWT)
    ///
    /// 기능 (ChatScene과 동일)
    ///  - 시작: 로비에서 들어온 방(RoomSession)으로 바로 채팅. 단독 실행 시 [개발용] 자동 로그인 → 방 참가(없으면 생성)
    ///  - pollInterval초마다 afterId 이후 새 메시지만 받아 기록 뒤에 붙이고, N회마다 전체 목록으로 동기화
    ///  - 서버 시스템 메시지(입장·퇴장, 게임 진행 안내, 공지) → [시스템] + Unity 콘솔 [Chat][시스템]
    ///  - 내 화면 전용 안내(ChatNotice: 로그인, 방 입장, 연결 상태 등) → [안내] + Unity 콘솔 [Chat][안내]
    ///  - 오류는 채팅용 문장으로 바꿔 표시하고 콘솔에는 원문도 남김. 401이면 다시 로그인
    ///  - SendSystemMessage(): 공지 전송 (에디터 창 Tools > Chat > System Message Console)
    ///
    /// 글꼴은 ChatLogView의 줄 프리팹(ChatLogLine) 것을 그대로 쓰고, 글자색은 ChatScene 설정을 씁니다.
    /// </summary>
    public class GameChatController : MonoBehaviour, IChatSystemSender
    {
        [Header("References")]
        public ChatApiClient api;
        public ChatLogView chatLog;
        public BottomTabController bottomTab;

        [Header("Chat")]
        [Tooltip("화면에 남겨 두는 최근 메시지 수 (서버 조회 limit, 최대 200)")]
        public int maxMessages = 50;
        [Tooltip("내 userId. -1이면 로그인 세션(AuthSession.UserId)이나 첫 전송 응답으로 자동 설정")]
        public long myUserId = -1;

        [Header("[개발용] 단독 실행 (로비 없이 이 씬만 플레이할 때)")]
        public bool autoLogin = true;
        public string devUsername = "tester1";
        public string devPassword = "Test1234!";
        public bool autoJoinRoom = true;
        public bool createRoomIfMissing = true;
        public string devRoomTitle = "채팅 테스트방";
        public int devRoomMaxPlayers = 8;

        [Header("Polling (새 메시지 자동 수신)")]
        public float pollInterval = 2f;
        public int fullSyncEveryPolls = 15;
        public float retryInterval = 3f;

        [Header("Style (ChatScene과 같은 색)")]
        public Color myNameColor = new Color(0.45f, 0.8f, 1f);
        public Color otherNameColor = new Color(1f, 0.85f, 0.4f);
        public Color errorColor = new Color(1f, 0.45f, 0.45f);
        public Color systemColor = new Color(0.4f, 0.9f, 0.45f);
        public string systemPrefix = "[시스템] ";
        public Color localNoticeColor = new Color(0.62f, 0.91f, 0.63f);
        public string localNoticePrefix = "[안내] ";

        [Header("Console (Unity 콘솔 연동)")]
        public bool logSystemMessagesToConsole = true;

        // ---------- 상태 ----------
        readonly List<ChatMessage> _messages = new List<ChatMessage>();
        bool _ready, _connecting, _sending, _sendingSystem;
        bool _fetching, _fetchAgain, _fullSyncRequested;
        bool _stickToBottom = true;
        bool _joinBlockedByGame;
        long _lastId = -1;
        long _lastLoggedSystemId = -1;
        float _nextPollTime;
        int _pollCount;
        string _lastStatus = ""; // 마지막으로 표시한 일시 오류 (같은 오류 반복 표시 방지)
        bool _failed;            // 다시 시도해도 안 되는 오류로 멈춤

        // 내 화면 전용 줄(안내·오류). anchor = 생겼을 때 마지막으로 받은 메시지 id (그 메시지 뒤에 표시)
        class LocalLine
        {
            public long seq;
            public long anchor;
            public string rich;
        }
        const long UnresolvedAnchor = long.MinValue;
        readonly List<LocalLine> _locals = new List<LocalLine>();
        long _localSeq;

        // ChatLogView에 이미 그린 줄의 키 (뒤에 붙이기만 하면 되는지, 다시 그려야 하는지 판단)
        readonly List<string> _renderedKeys = new List<string>();
        ScrollRect _scroll;

        static readonly Regex NoParseCloseTag = new Regex("</\\s*noparse\\s*>", RegexOptions.IgnoreCase);

        public bool IsReady { get { return _ready; } }
        public long RoomId { get { return api != null ? api.roomId : 0; } }

        // ================= Unity =================

        void Awake()
        {
            if (api == null) api = GetComponent<ChatApiClient>();
            if (chatLog == null) chatLog = FindFirstObjectByType<ChatLogView>(FindObjectsInactive.Include);
            if (bottomTab == null) bottomTab = FindFirstObjectByType<BottomTabController>(FindObjectsInactive.Include);
            if (chatLog != null) _scroll = chatLog.GetComponentInChildren<ScrollRect>(true);
        }

        void OnEnable()
        {
            ChatNotice.Posted += OnChatNotice;
            if (bottomTab != null) bottomTab.ChatSubmitted += Send;
        }

        void OnDisable()
        {
            ChatNotice.Posted -= OnChatNotice;
            if (bottomTab != null) bottomTab.ChatSubmitted -= Send;
        }

        void Start()
        {
            if (api == null || chatLog == null)
            {
                Debug.LogError("[Chat] GameChatController에 ChatApiClient 또는 ChatLogView가 연결되지 않았습니다.");
                enabled = false;
                return;
            }
            chatLog.Clear(); // 씬에 미리 들어 있던 예시 줄 제거
            foreach (var text in ChatNotice.TakePending()) AddLocal(Colored(localNoticePrefix + text, localNoticeColor));
            Render();
            StartCoroutine(Connect());
        }

        void Update()
        {
            if (!_ready || pollInterval <= 0f || Time.unscaledTime < _nextPollTime) return;
            _nextPollTime = Time.unscaledTime + pollInterval;
            if (_fetching) return;
            _pollCount++;
            RequestFetch(fullSyncEveryPolls > 0 && _pollCount % fullSyncEveryPolls == 0);
        }

        // ================= 연결 (로그인 → 방 참가) =================

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
                    myUserId = AuthSession.UserId;

                // 1) 로그인 (타이틀/로비에서 이미 로그인했다면 건너뜀)
                if (!AuthSession.IsAuthenticated)
                {
                    if (!autoLogin) { Fail("로그인 후 이용할 수 있습니다.", "AuthSession에 토큰이 없고 [개발용] autoLogin이 꺼져 있음"); break; }
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
                        if (code >= 400 && code < 500) { Fail(err); break; }
                        SetError(Friendly(err, code), err);
                        yield return new WaitForSecondsRealtime(retryInterval);
                        continue;
                    }
                }

                // 2) 방: 로비에서 입장한 방이 있으면 그대로 사용
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
                            if (code == 401) { NotifyExpired(); continue; }
                            if (code == 409)
                            {
                                if (err.Contains("가득")) { Fail("방이 가득 차 입장할 수 없습니다.", err); break; }
                                if (err.Contains("게임이 진행 중")) { _joinBlockedByGame = true; Debug.Log("[Chat] " + err); }
                                else ChatNotice.Post("이미 참가 중인 방입니다. 채팅을 이어서 진행합니다.");
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
                _failed = false;
                _lastStatus = "";
                _nextPollTime = Time.unscaledTime + pollInterval;
                RequestFetch(true);
                break;
            }

            _connecting = false;
        }

        void HandleUnauthorized()
        {
            _ready = false;
            NotifyExpired();
            StartCoroutine(Connect());
        }

        void NotifyExpired()
        {
            AuthSession.Clear();
            ChatNotice.Post("로그인 시간이 만료되었습니다. 다시 로그인해 주세요.");
        }

        // ================= 전송 =================

        /// 하단 탭에서 채팅을 보냈을 때 (BottomTabController.ChatSubmitted)
        public void Send(string text)
        {
            text = text == null ? "" : text.Trim();
            if (text.Length == 0 || _sending) return;
            if (!_ready)
            {
                SetError(_failed ? "채팅을 사용할 수 없습니다." : "서버와 연결이 끊어졌습니다. 다시 연결하는 중입니다...");
                return;
            }

            _sending = true;
            StartCoroutine(api.PostMessage(text,
                senderId =>
                {
                    _sending = false;
                    if (senderId >= 0 && senderId != myUserId) myUserId = senderId;
                    _lastStatus = "";
                    _stickToBottom = true; // 내가 보낸 메시지는 항상 보이도록
                    RequestFetch();
                },
                (err, code) =>
                {
                    _sending = false;
                    // 입력칸은 BottomTabController가 이미 비웠으므로 보내지 못한 내용을 안내에 남긴다.
                    SetError(Friendly(err, code) + " (보내지 못한 메시지: " + text + ")", err);
                    if (code == 401) HandleUnauthorized();
                }));
        }

        /// 시스템 메시지(공지) 전송. 채팅창에는 서버 조회 결과로 [시스템]이 표시되고 콘솔에도 출력됩니다.
        public void SendSystemMessage(string text, Action<bool, string> onDone = null)
        {
            text = text == null ? "" : text.Trim();
            string reason = null;
            if (text.Length == 0) reason = "보낼 내용이 없습니다.";
            else if (_sendingSystem) reason = "이전 공지를 보내는 중입니다.";
            else if (!_ready || !AuthSession.IsAuthenticated) reason = "서버에 연결된 뒤 공지를 보낼 수 있습니다.";
            if (reason != null)
            {
                if (_ready && AuthSession.IsAuthenticated) Debug.LogWarning("[Chat] 공지 전송 불가: " + reason);
                else ChatNotice.Post(reason);
                if (onDone != null) onDone(false, reason);
                return;
            }

            _sendingSystem = true;
            StartCoroutine(api.PostSystemMessage(text,
                () =>
                {
                    _sendingSystem = false;
                    _stickToBottom = true;
                    RequestFetch();
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

        // ================= 조회 =================

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

        void OnFullListReceived(List<ChatMessage> list)
        {
            _lastStatus = "";
            var sorted = SortOldestFirst(list).ToList();
            if (SameIds(sorted, _messages)) return;

            if (MaxId(sorted) < _lastLoggedSystemId) _lastLoggedSystemId = -1; // 서버/Redis 초기화
            _messages.Clear();
            _messages.AddRange(sorted);
            TrimToMax();
            _lastId = MaxId(_messages);
            ResolveAnchors();
            LogNewSystemMessages();
            Render();
        }

        void OnNewMessagesReceived(List<ChatMessage> list)
        {
            _lastStatus = "";
            bool added = false;
            foreach (var m in SortOldestFirst(list))
            {
                long id;
                bool hasId = long.TryParse(m.id, out id);
                if (hasId && id <= _lastId) continue;
                _messages.Add(m);
                if (hasId && id > _lastId) _lastId = id;
                added = true;
            }
            if (!added) return;
            TrimToMax();
            ResolveAnchors();
            LogNewSystemMessages();
            Render();
        }

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

        // ================= 내 화면 전용 줄 =================

        void OnChatNotice(string text)
        {
            AddLocal(Colored(localNoticePrefix + text, localNoticeColor));
            _stickToBottom = true;
            Render();
        }

        void AddLocal(string rich)
        {
            _locals.Add(new LocalLine { seq = ++_localSeq, anchor = _lastId >= 0 ? _lastId : UnresolvedAnchor, rich = rich });
            if (_locals.Count > maxMessages) _locals.RemoveRange(0, _locals.Count - maxMessages);
        }

        void ResolveAnchors()
        {
            foreach (var l in _locals)
                if (l.anchor == UnresolvedAnchor) l.anchor = _lastId;
        }

        /// 일시적인 오류. 같은 문장이 연속되면 다시 표시하지 않습니다. (콘솔에는 원문도 남김)
        void SetError(string text, string detail = null)
        {
            if (text == _lastStatus) return;
            _lastStatus = text;
            Debug.LogWarning("[Chat] " + text + (string.IsNullOrEmpty(detail) || detail == text ? "" : "  (" + detail + ")"));
            AddLocal(Colored(text, errorColor));
            _stickToBottom = true;
            Render();
        }

        /// 다시 시도해도 해결되지 않는 오류
        void Fail(string text, string detail = null)
        {
            _failed = true;
            SetError(text, detail);
        }

        string Friendly(string err, long code)
        {
            if (code == 0) return "서버와 연결이 끊어졌습니다. 다시 연결하는 중입니다...";
            if (code == 401) return "로그인 시간이 만료되었습니다. 다시 로그인해 주세요.";
            if (code == 403 && err != null && err.StartsWith("전송 실패"))
                return _joinBlockedByGame ? "게임이 진행 중이라 지금은 입장할 수 없습니다." : "지금은 채팅에 참여할 수 없습니다.";
            return err;
        }

        // ================= 표시 (ChatLogView) =================

        /// 메시지와 내 화면 전용 줄을 순서대로 합친 목록을 만들고,
        /// 이미 그린 줄 뒤에 붙이기만 하면 되면 붙이고, 순서가 바뀌었으면(전체 동기화 등) 다시 그립니다.
        void Render()
        {
            if (chatLog == null) return;

            var keys = new List<string>();
            var lines = new List<string>();
            string myHex = ColorUtility.ToHtmlStringRGB(myNameColor);
            string otherHex = ColorUtility.ToHtmlStringRGB(otherNameColor);
            string myId = myUserId >= 0 ? myUserId.ToString(CultureInfo.InvariantCulture) : null;
            int li = 0;

            foreach (var m in _messages)
            {
                long mid;
                if (long.TryParse(m.id, out mid))
                    while (li < _locals.Count && _locals[li].anchor != UnresolvedAnchor && _locals[li].anchor < mid)
                    {
                        keys.Add("l" + _locals[li].seq); lines.Add(_locals[li].rich); li++;
                    }

                keys.Add("m" + m.id);
                if (m.IsSystem)
                {
                    lines.Add(Colored(systemPrefix + m.content, systemColor));
                }
                else
                {
                    bool mine = myId != null && m.userId == myId;
                    lines.Add("<color=#" + (mine ? myHex : otherHex) + "><b>"
                              + NoParse(string.IsNullOrEmpty(m.nickname) ? "?" : m.nickname)
                              + "</b></color>: " + NoParse(m.content));
                }
            }
            for (; li < _locals.Count; li++) { keys.Add("l" + _locals[li].seq); lines.Add(_locals[li].rich); }

            bool atBottom = _scroll == null || _scroll.verticalNormalizedPosition <= 0.01f
                            || _scroll.content == null || _scroll.viewport == null
                            || _scroll.content.rect.height <= _scroll.viewport.rect.height + 1f;

            bool appendOnly = keys.Count >= _renderedKeys.Count;
            for (int i = 0; appendOnly && i < _renderedKeys.Count; i++)
                if (keys[i] != _renderedKeys[i]) appendOnly = false;

            if (!appendOnly)
            {
                chatLog.Clear();
                _renderedKeys.Clear();
            }
            for (int i = _renderedKeys.Count; i < keys.Count; i++)
            {
                chatLog.AddLine(lines[i]);
                _renderedKeys.Add(keys[i]);
            }

            if (atBottom || _stickToBottom) chatLog.ScrollToLatest();
            _stickToBottom = false;
        }

        // ================= 유틸 =================

        void TrimToMax()
        {
            if (_messages.Count > maxMessages) _messages.RemoveRange(0, _messages.Count - maxMessages);
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
            return list;
        }

        static string Colored(string text, Color color)
        {
            return "<color=#" + ColorUtility.ToHtmlStringRGB(color) + ">" + NoParse(text) + "</color>";
        }

        static string NoParse(string s)
        {
            return "<noparse>" + NoParseCloseTag.Replace(s ?? "", "</no parse>") + "</noparse>";
        }
    }
}
