using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using WhoisntCitizen.Network.Realtime;

namespace WhoisntCitizen.Network
{
    /// <summary>
    /// 서버 WebSocket(/ws/v1)과의 연결 하나를 맡는다. 게임 전체에서 하나만 있고 씬이 바뀌어도 유지된다.
    ///
    /// 하는 일
    ///  - 연결: ClientWebSocket + Authorization: Bearer 헤더. 끊기면 ReconnectBackoff(1→2→4…30초)로 다시 연결.
    ///  - 수신: 백그라운드 스레드에서 받은 텍스트를 큐에 넣고, Update(메인 스레드)에서 꺼내 해석·처리한다.
    ///          → 이벤트를 받는 화면 코드는 Unity API를 마음껏 써도 된다.
    ///  - ping: 서버가 connected를 보낸 뒤 10초마다. 서버는 30초 동안 조용하면 끊는다.
    ///  - 상태: RealtimeConnectionTracker에 사건을 알리고, 돌려받은 Subscribe/Unsubscribe를 보낸다.
    ///
    /// 내용을 주고받지 않는다. 서버는 "바뀌었다"는 신호만 보내고, 화면이 기존 REST GET으로 가져간다.
    ///
    /// 쓰는 쪽
    ///  - 연결: Connect() / Disconnect()
    ///  - 방: EnterRoom(roomId) / LeaveRoom() — 끊겨 있어도 기억했다가 연결되면 구독한다.
    ///  - 이벤트: Resynced(구독 확인 → 전부 GET 한 번)
    ///            RoomChanged / GameChanged / ChatChanged(그 부분만 GET) — 걸러지고 프레임당 종류별 한 번
    ///            StateChanged(폴링 주기 조정), MessageReceived(모든 메시지, 디버깅용)
    ///            AuthExpired(토큰 만료 → 재연결 멈춤, 다시 로그인 필요), SubscribeRejected(NOT_IN_ROOM)
    ///  - 실패 처리 규칙은 RealtimeFailurePolicy (4401 멈춤, 4408·그 밖 재연결, NOT_IN_ROOM 거절, SERVER_ERROR 재시도)
    /// </summary>
    public sealed class RealtimeClient : MonoBehaviour
    {
        // ================================================================ 설정

        /// <summary>ping·pong까지 Console에 찍는다. 평소엔 10초마다 찍혀 시끄러우니 끈다.</summary>
        public static bool LogHeartbeat = false;

        /// <summary>서버 메시지 하나의 최대 크기. 서버 메시지는 수십 바이트라 넉넉하다. 넘으면 이상한 연결로 보고 끊는다.</summary>
        private const int MaxIncomingBytes = 64 * 1024;

        /// <summary>
        /// 이 시간(초) 동안 서버에서 아무것도 안 오면 연결이 조용히 죽은 것으로 보고 끊는다.
        /// 10초마다 ping → pong이 오므로 정상이면 절대 넘지 않는다. (서버의 30초 기준보다 조금 짧게)
        /// </summary>
        private const double ReceiveTimeoutSeconds = 25.0;

        // ================================================================ 싱글턴

        private static RealtimeClient instance;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            instance = null;
            LogHeartbeat = false;
        }

        /// <summary>하나뿐인 클라이언트. 없으면 만든다(DontDestroyOnLoad). 플레이 중에만 부른다.</summary>
        public static RealtimeClient Instance
        {
            get
            {
                if (instance == null)
                {
                    var go = new GameObject("[RealtimeClient]");
                    instance = go.AddComponent<RealtimeClient>();
                    DontDestroyOnLoad(go);
                }
                return instance;
            }
        }

        /// <summary>이미 만들어져 있을 때만 돌려준다(없으면 null). 화면이 OnDestroy에서 구독을 풀 때처럼, 새로 만들면 안 될 때 쓴다.</summary>
        public static RealtimeClient ExistingInstance => instance;

        // ================================================================ 공개 상태·이벤트

        private readonly RealtimeConnectionTracker tracker = new RealtimeConnectionTracker();
        private readonly ReconnectBackoff backoff = new ReconnectBackoff();
        private readonly RealtimeSignalFilter signalFilter = new RealtimeSignalFilter();

        /// <summary>연결 상태와 구독 중인 방. 화면은 읽기만 한다.</summary>
        public RealtimeConnectionTracker Tracker => tracker;

        public RealtimeState State => tracker.State;

        /// <summary>상태가 바뀔 때 (이전, 이후). 메인 스레드에서 불린다.</summary>
        public event Action<RealtimeState, RealtimeState> StateChanged;

        /// <summary>서버 메시지를 받을 때마다(해석에 성공한 것만). 메인 스레드에서 불린다.</summary>
        public event Action<RealtimeServerMessage> MessageReceived;

        /// <summary>
        /// 서버가 이 방의 구독을 확인했다(subscribed). 처음 구독할 때와 재연결 뒤 다시 구독할 때마다 한 번씩 온다.
        /// 받은 화면은 방·게임·채팅을 GET으로 한 번 가져와야 한다.
        /// 구독 전이나 끊긴 동안 생긴 변경은 신호로 오지 않았기 때문이다. 메인 스레드에서 불린다.
        /// </summary>
        public event Action<long> Resynced;

        /// <summary>방 정보가 바뀌었다 (roomId) → GET /api/v1/rooms/{roomId}. 메인 스레드, 프레임당 최대 한 번.</summary>
        public event Action<long> RoomChanged;

        /// <summary>게임 상태가 바뀌었다 (gameId, phaseVersion) → GET /api/v1/games/{gameId}. 메인 스레드, 프레임당 최대 한 번.</summary>
        public event Action<string, long> GameChanged;

        /// <summary>
        /// 새 채팅이 생겼다 (roomId, lastMessageId) → GET messages?afterId=(내가 가진 마지막 id).
        /// 화면이 이미 lastMessageId까지 갖고 있으면 무시해도 된다. 메인 스레드, 프레임당 최대 한 번.
        /// </summary>
        public event Action<long, long> ChatChanged;

        /// <summary>
        /// 토큰이 만료돼 서버가 연결을 거절했다(4401 또는 핸드셰이크 401). 재연결은 멈춘 상태다.
        /// 받은 쪽은 다시 로그인하게 하고, 새 토큰을 받은 뒤 Connect()를 다시 부른다. 메인 스레드에서 불린다.
        /// </summary>
        public event Action AuthExpired;

        /// <summary>
        /// 서버가 방 구독을 거절했다(NOT_IN_ROOM) (거절된 roomId). 이 방의 신호는 오지 않고 빠른 폴링으로 돌아간다.
        /// 방 화면은 바로 GET해서 실제로 방에서 나가졌는지(강퇴·방 삭제) 확인한다. 메인 스레드에서 불린다.
        /// </summary>
        public event Action<long> SubscribeRejected;

        /// <summary>토큰 만료로 멈춘 상태인가. Connect()를 다시 부르면 false.</summary>
        public bool IsAuthExpired { get; private set; }

        /// <summary>화면이 보고 있는 방(EnterRoom으로 알린 방). 방 밖이면 0.</summary>
        public long CurrentRoomId => tracker.DesiredRoomId;

        /// <summary>이 방의 신호를 지금 받고 있나. true면 폴링을 안전망(15초)으로 늦춰도 된다.</summary>
        public bool ReceivesSignalsFor(long roomId) => tracker.ReceivesSignalsFor(roomId);

        /// <summary>Connect()를 불러 연결을 원하는 상태인가. Disconnect()하면 false.</summary>
        public bool WantsConnection => wantConnected;

        /// <summary>마지막 pong에서 계산한 (서버 시각 - 기기 시각) ms. 아직 모르면 null. 참고용.</summary>
        public long? ServerClockOffsetMs { get; private set; }

        // ================================================================ 내부 상태 (메인 스레드 전용)

        private bool wantConnected;
        private int connectionSeq;          // 연결 시도마다 1씩. 이전 소켓에서 늦게 온 사건을 걸러 낸다.
        private ClientWebSocket socket;     // 현재 소켓. 없으면 null.
        private CancellationTokenSource socketCts;
        private SemaphoreSlim sendLock;     // ClientWebSocket은 동시에 SendAsync를 하나만 허용한다.
        private bool socketOpen;            // 핸드셰이크 성공 후 true
        private double nextConnectAt;       // 이 시각(realtime) 이후 다음 연결 시도
        private double nextPingAt;
        private double lastReceivedAt;
        private double nextResubscribeAt;   // SERVER_ERROR 뒤 구독 재시도 시각. 0이면 예약 없음.

        // 백그라운드 스레드 → 메인 스레드
        private readonly ConcurrentQueue<SocketEvent> inbox = new ConcurrentQueue<SocketEvent>();

        private enum SocketEventKind { Opened, Message, Closed }

        private sealed class SocketEvent
        {
            public SocketEventKind Kind;
            public int Seq;
            public string Text;          // Message
            public int? CloseCode;       // Closed: 서버가 보낸 종료 코드(4401, 4408 등). 네트워크 오류면 null.
            public string Reason;        // Closed: 종료 이유 또는 예외 메시지
            public bool Unauthorized;    // Closed: 핸드셰이크에서 HTTP 401을 받았다
        }

        private static double Now => Time.realtimeSinceStartupAsDouble;

        // ================================================================ Unity

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }
            instance = this;
            tracker.StateChanged += OnTrackerStateChanged;
        }

        private void Update()
        {
            DrainInbox();
            RaiseSignals();

            double now = Now;

            if (wantConnected && socket == null && tracker.State == RealtimeState.Disconnected && now >= nextConnectAt)
            {
                StartConnect();
            }

            if (nextResubscribeAt > 0 && now >= nextResubscribeAt)
            {
                nextResubscribeAt = 0;
                if (tracker.State == RealtimeState.Connected && tracker.DesiredRoomId > 0)
                {
                    Log("구독 재시도: room " + tracker.DesiredRoomId);
                    Execute(tracker.EnterRoom(tracker.DesiredRoomId));
                }
            }

            if (socketOpen)
            {
                if (now - lastReceivedAt > ReceiveTimeoutSeconds)
                {
                    Log($"서버 응답이 {ReceiveTimeoutSeconds:0}초 동안 없어 연결을 다시 맺습니다.");
                    AbortSocket();
                    HandleClosed(null, "receive timeout");
                }
                else if (now >= nextPingAt)
                {
                    nextPingAt = now + RealtimeProtocol.HeartbeatIntervalSeconds;
                    Send(RealtimeMessageCodec.EncodePing(), heartbeat: true);
                }
            }
        }

        private void OnDestroy()
        {
            if (instance == this)
            {
                instance = null;
            }
            tracker.StateChanged -= OnTrackerStateChanged;
            wantConnected = false;
            AbortSocket();
        }

        // ================================================================ 공개 API

        /// <summary>
        /// 연결을 시작한다(이미 원하는 상태면 아무 일도 없다). 로그인 직후에 부른다.
        /// 끊기면 Disconnect()를 부를 때까지 알아서 다시 연결한다.
        /// </summary>
        public void Connect()
        {
            if (wantConnected)
            {
                return;
            }
            wantConnected = true;
            IsAuthExpired = false;
            backoff.Reset();
            nextConnectAt = 0; // 다음 Update에서 바로
        }

        /// <summary>연결을 끊고 다시 연결하지 않는다. 로그아웃·타이틀 복귀 때 부른다.</summary>
        public void Disconnect()
        {
            wantConnected = false;
            CloseSocketGracefully();
            signalFilter.Clear();
            if (tracker.State != RealtimeState.Disconnected)
            {
                tracker.OnDisconnected();
            }
        }

        /// <summary>
        /// 방에 들어갔다(방 화면·게임 화면 진입). 연결돼 있으면 바로 room.subscribe를 보내고,
        /// 끊겨 있거나 연결 중이면 기억해 두었다가 connected를 받는 순간 보낸다.
        /// 같은 방을 이미 구독 중이면 아무것도 보내지 않는다(Room → GameScene 전환).
        /// </summary>
        public void EnterRoom(long roomId)
        {
            if (roomId <= 0)
            {
                Debug.LogWarning("[Realtime] EnterRoom: 잘못된 방 번호라 무시합니다: " + roomId);
                return;
            }
            nextResubscribeAt = 0;
            Execute(tracker.EnterRoom(roomId));
        }

        /// <summary>
        /// 방에서 나갔다(퇴장·강퇴·방 삭제·로비 복귀). 구독 중이면 room.unsubscribe를 보낸다.
        /// 연결은 끊지 않는다. 방에 들어가 있지 않으면 아무 일도 없다.
        /// </summary>
        public void LeaveRoom()
        {
            nextResubscribeAt = 0;
            Execute(tracker.LeaveRoom());
            signalFilter.Clear();
        }

        // ================================================================ 연결

        private void StartConnect()
        {
            string token = AuthSession.AccessToken;
            if (string.IsNullOrEmpty(token))
            {
                // 로그인 전이면 시도해도 401이다. 1초 뒤 다시 확인한다. (실패가 아니므로 백오프는 늘리지 않는다)
                nextConnectAt = Now + 1.0;
                return;
            }
            if (RealtimeFailurePolicy.IsTokenExpired(AuthSession.TokenMinutesLeft))
            {
                // 연결해 봐야 401이다. 서버에 헛걸음하지 않고 바로 알린다.
                StopForAuthExpired("토큰이 이미 만료됐습니다");
                return;
            }

            Uri uri;
            try
            {
                uri = new Uri(RealtimeProtocol.BuildUrl(ApiConfig.BaseUrl));
            }
            catch (Exception e)
            {
                Debug.LogError("[Realtime] WebSocket 주소를 만들 수 없습니다: " + e.Message);
                wantConnected = false;
                return;
            }

            int seq = ++connectionSeq;
            var ws = new ClientWebSocket();
            ws.Options.SetRequestHeader("Authorization", "Bearer " + token);
            // 연결 유지는 앱 수준 ping(10초)으로 한다. 서버는 텍스트 메시지만 "살아 있음"으로 센다.
            ws.Options.KeepAliveInterval = TimeSpan.Zero;

            socket = ws;
            socketCts = new CancellationTokenSource();
            sendLock = new SemaphoreSlim(1, 1);
            socketOpen = false;

            tracker.OnConnecting();
            Log($"연결 시도: {uri} (재시도 {backoff.Attempt}회째)");

            CancellationToken ct = socketCts.Token;
            int connectTimeoutMs = Mathf.Max(1, ApiConfig.TimeoutSeconds) * 1000;
            Task.Run(() => RunSocketAsync(ws, uri, seq, connectTimeoutMs, ct));
        }

        /// <summary>백그라운드 스레드. 연결 → 수신 반복 → 끝나면 Closed 한 번. Unity API를 쓰지 않는다.</summary>
        private async Task RunSocketAsync(ClientWebSocket ws, Uri uri, int seq, int connectTimeoutMs, CancellationToken ct)
        {
            int? closeCode = null;
            string reason = null;
            bool opened = false;
            bool unauthorized = false;
            try
            {
                using (var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct))
                {
                    connectCts.CancelAfter(connectTimeoutMs);
                    await ws.ConnectAsync(uri, connectCts.Token).ConfigureAwait(false);
                }
                opened = true;
                inbox.Enqueue(new SocketEvent { Kind = SocketEventKind.Opened, Seq = seq });

                var buffer = new byte[4096];
                using (var message = new MemoryStream())
                {
                    while (!ct.IsCancellationRequested && ws.State == WebSocketState.Open)
                    {
                        WebSocketReceiveResult result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct).ConfigureAwait(false);

                        if (result.MessageType == WebSocketMessageType.Close)
                        {
                            closeCode = (int?)result.CloseStatus;
                            reason = result.CloseStatusDescription;
                            break;
                        }

                        message.Write(buffer, 0, result.Count);
                        if (message.Length > MaxIncomingBytes)
                        {
                            reason = "message too large";
                            break;
                        }
                        if (!result.EndOfMessage)
                        {
                            continue;
                        }

                        if (result.MessageType == WebSocketMessageType.Text)
                        {
                            string text = Encoding.UTF8.GetString(message.GetBuffer(), 0, (int)message.Length);
                            inbox.Enqueue(new SocketEvent { Kind = SocketEventKind.Message, Seq = seq, Text = text });
                        }
                        message.SetLength(0);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                reason = ct.IsCancellationRequested ? "cancelled" : "connect timeout";
            }
            catch (Exception e)
            {
                reason = e.GetBaseException().Message;
                // 핸드셰이크 거절(401)은 상태 코드가 든 예외 메시지로만 알 수 있다.
                unauthorized = !opened
                    && (RealtimeFailurePolicy.IsUnauthorizedHandshake(e.Message)
                        || RealtimeFailurePolicy.IsUnauthorizedHandshake(reason));
            }
            finally
            {
                if (closeCode == null && ws.CloseStatus.HasValue)
                {
                    closeCode = (int)ws.CloseStatus.Value;
                    reason = reason ?? ws.CloseStatusDescription;
                }
                inbox.Enqueue(new SocketEvent
                {
                    Kind = SocketEventKind.Closed, Seq = seq, CloseCode = closeCode, Reason = reason, Unauthorized = unauthorized
                });
                ws.Dispose();
            }
        }

        // ================================================================ 메인 스레드 처리

        private void DrainInbox()
        {
            while (inbox.TryDequeue(out SocketEvent e))
            {
                if (e.Seq != connectionSeq)
                {
                    continue; // 이미 버린 소켓에서 늦게 온 사건
                }
                switch (e.Kind)
                {
                    case SocketEventKind.Opened:
                        HandleOpened();
                        break;
                    case SocketEventKind.Message:
                        HandleMessage(e.Text);
                        break;
                    case SocketEventKind.Closed:
                        ReleaseSocket();
                        HandleClosed(e.CloseCode, e.Reason, e.Unauthorized);
                        break;
                }
            }
        }

        private void HandleOpened()
        {
            socketOpen = true;
            double now = Now;
            lastReceivedAt = now;
            nextPingAt = now + RealtimeProtocol.HeartbeatIntervalSeconds;
            Log("소켓 열림. connected를 기다립니다.");
        }

        private void HandleMessage(string text)
        {
            lastReceivedAt = Now;

            if (!RealtimeMessageCodec.TryDecode(text, out RealtimeServerMessage message, out string error))
            {
                Debug.LogWarning("[Realtime] 해석할 수 없는 메시지를 무시합니다: " + error);
                return;
            }

            switch (message)
            {
                case ConnectedMessage connected:
                    backoff.Reset();
                    Log($"connected: userId={connected.UserId}");
                    Execute(tracker.OnConnected(connected.UserId));
                    break;

                case PongMessage pong:
                    ServerClockOffsetMs = pong.ServerTimeMs - DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                    if (LogHeartbeat) Log("pong");
                    break;

                case SubscribedMessage subscribed:
                    bool resync = tracker.OnSubscribed(subscribed.RoomId);
                    Log($"subscribed: room={subscribed.RoomId}" + (resync ? " → 한 번 GET(Resynced)" : " (무시: 떠난 방이거나 중복)"));
                    if (resync)
                    {
                        signalFilter.OnResubscribed(); // 어차피 전부 GET하므로 모아 둔 신호는 버린다
                        RaiseResynced(subscribed.RoomId);
                    }
                    break;

                case RoomChangedMessage _:
                case GameChangedMessage _:
                case ChatChangedMessage _:
                    if (!signalFilter.Offer(message, tracker.SubscribedRoomId))
                    {
                        Log("신호 무시(구독 전·다른 방·이미 받은 채팅): " + Describe(message));
                    }
                    else
                    {
                        Log("신호: " + Describe(message));
                    }
                    break;

                case RealtimeErrorMessage err:
                    HandleServerError(err);
                    break;

                case UnknownServerMessage unknown:
                    Log("모르는 메시지 type을 무시합니다: " + unknown.Type);
                    break;

                default:
                    Log(message.Type);
                    break;
            }

            try
            {
                MessageReceived?.Invoke(message);
            }
            catch (Exception e)
            {
                // 받는 화면 하나의 오류가 연결 처리를 멈추면 안 된다.
                Debug.LogException(e);
            }
        }

        private void HandleClosed(int? closeCode, string reason, bool handshakeUnauthorized = false)
        {
            socketOpen = false;
            nextResubscribeAt = 0;
            signalFilter.Clear();
            tracker.OnDisconnected();

            if (!wantConnected)
            {
                Log($"연결 종료 (code={closeCode?.ToString() ?? "-"}, {reason})");
                return;
            }

            if (RealtimeFailurePolicy.ForClose(closeCode, handshakeUnauthorized) == CloseAction.StopAuthExpired)
            {
                StopForAuthExpired(handshakeUnauthorized ? "연결 거절(401)" : $"서버가 연결을 닫음({closeCode})");
                return;
            }

            if (closeCode == RealtimeProtocol.CloseIdleTimeout)
            {
                // 보통 앱이 잠시 멈춰(창 비활성·디버거 정지) ping을 못 보낸 경우다. 평소처럼 다시 연결한다.
                Log("서버가 응답 없음(4408)으로 연결을 닫았습니다. ping이 멈췄었는지 확인하세요(Run In Background).");
            }

            double delay = backoff.NextDelaySeconds();
            nextConnectAt = Now + delay;
            Log($"연결 끊김 (code={closeCode?.ToString() ?? "-"}, {reason}) → {delay:0.0}초 뒤 다시 연결");
        }

        /// <summary>토큰 만료: 재연결을 멈추고 AuthExpired를 알린다. 다시 로그인한 뒤 Connect()로 재개한다.</summary>
        private void StopForAuthExpired(string why)
        {
            wantConnected = false;
            IsAuthExpired = true;
            nextResubscribeAt = 0;
            Debug.LogWarning($"[Realtime] 토큰이 만료돼 실시간 연결을 멈춥니다: {why}. 다시 로그인해야 합니다. (그동안 화면은 폴링으로만 동작)");
            try
            {
                AuthExpired?.Invoke();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        private void HandleServerError(RealtimeErrorMessage err)
        {
            switch (RealtimeFailurePolicy.ForError(err.Code))
            {
                case ErrorAction.SubscribeRejected:
                    long roomId = tracker.DesiredRoomId;
                    nextResubscribeAt = 0;
                    tracker.OnSubscribeRejected();
                    signalFilter.Clear();
                    Debug.LogWarning($"[Realtime] 방 {roomId} 구독이 거절됐습니다({err.Code}). 폴링으로 계속합니다. {err.Message}");
                    if (roomId > 0)
                    {
                        Invoke(SubscribeRejected, roomId);
                    }
                    break;

                case ErrorAction.RetrySubscribeLater:
                    Debug.LogWarning($"[Realtime] 서버 오류({err.Code}): {err.Message}");
                    // 기다리던 구독 요청은 실패했다 → 재시도 때 EnterRoom이 다시 보내도록 '답 대기'를 풀어 둔다.
                    if (tracker.State == RealtimeState.Connected) tracker.OnSubscribeRejected();
                    if (tracker.State == RealtimeState.Connected && tracker.DesiredRoomId > 0)
                    {
                        nextResubscribeAt = Now + RealtimeFailurePolicy.RetrySubscribeDelaySeconds;
                        Log($"{RealtimeFailurePolicy.RetrySubscribeDelaySeconds:0}초 뒤 방 {tracker.DesiredRoomId} 구독을 다시 시도합니다.");
                    }
                    break;

                default:
                    // BAD_MESSAGE·UNKNOWN_TYPE은 클라이언트가 잘못 보낸 것이다. 다시 보내도 같으니 기록만 한다.
                    Debug.LogWarning($"[Realtime] 서버가 메시지를 처리하지 못했습니다({err.Code}): {err.Message}");
                    break;
            }
        }

        private void Execute(RealtimeAction action)
        {
            switch (action.Kind)
            {
                case RealtimeActionKind.Subscribe:
                    Log("room.subscribe " + action.RoomId);
                    Send(RealtimeMessageCodec.EncodeSubscribe(action.RoomId));
                    break;
                case RealtimeActionKind.Unsubscribe:
                    Log("room.unsubscribe " + action.RoomId);
                    Send(RealtimeMessageCodec.EncodeUnsubscribe(action.RoomId));
                    break;
            }
        }

        /// <summary>이번 프레임에 모인 신호를 종류별로 한 번씩 알린다. 순서: 방 → 게임 → 채팅.</summary>
        private void RaiseSignals()
        {
            RealtimeSignals signals = signalFilter.TakePending();
            if (signals.IsEmpty)
            {
                return;
            }
            if (signals.RoomChanged)
            {
                Invoke(RoomChanged, signals.RoomId);
            }
            if (signals.Game != null)
            {
                try { GameChanged?.Invoke(signals.Game.GameId, signals.Game.PhaseVersion); }
                catch (Exception e) { Debug.LogException(e); }
            }
            if (signals.Chat != null)
            {
                try { ChatChanged?.Invoke(signals.Chat.RoomId, signals.Chat.LastMessageId); }
                catch (Exception e) { Debug.LogException(e); }
            }
        }

        private static void Invoke(Action<long> handler, long value)
        {
            try { handler?.Invoke(value); }
            catch (Exception e) { Debug.LogException(e); }
        }

        private static string Describe(RealtimeServerMessage message)
        {
            switch (message)
            {
                case RoomChangedMessage r: return $"room.changed room={r.RoomId}";
                case GameChangedMessage g: return $"game.changed game={g.GameId} v={g.PhaseVersion}";
                case ChatChangedMessage c: return $"chat.changed room={c.RoomId} last={c.LastMessageId}";
                default: return message.Type;
            }
        }

        private void RaiseResynced(long roomId)
        {
            try
            {
                Resynced?.Invoke(roomId);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        private void OnTrackerStateChanged(RealtimeState previous, RealtimeState next)
        {
            try
            {
                StateChanged?.Invoke(previous, next);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        // ================================================================ 보내기

        /// <summary>열린 소켓으로 텍스트 하나를 보낸다. 실패하면 수신 루프가 끊김을 알려 주므로 여기선 기록만 한다.</summary>
        private void Send(string text, bool heartbeat = false)
        {
            if (!socketOpen || socket == null)
            {
                return;
            }
            if (heartbeat && LogHeartbeat)
            {
                Log("ping");
            }

            ClientWebSocket ws = socket;
            SemaphoreSlim gate = sendLock;
            CancellationToken ct = socketCts.Token;
            byte[] bytes = Encoding.UTF8.GetBytes(text);

            Task.Run(async () =>
            {
                bool entered = false;
                try
                {
                    await gate.WaitAsync(ct).ConfigureAwait(false);
                    entered = true;
                    if (ws.State == WebSocketState.Open)
                    {
                        await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, ct).ConfigureAwait(false);
                    }
                }
                catch (Exception)
                {
                    // 소켓이 닫히는 중이면 SendAsync가 실패한다. 끊김은 수신 루프의 Closed로 처리된다.
                }
                finally
                {
                    if (entered)
                    {
                        try { gate.Release(); } catch (ObjectDisposedException) { }
                    }
                }
            });
        }

        // ================================================================ 정리

        /// <summary>정상 종료(1000)를 보내고 소켓을 놓는다. 이후 그 소켓의 사건은 무시된다.</summary>
        private void CloseSocketGracefully()
        {
            ClientWebSocket ws = socket;
            CancellationTokenSource cts = socketCts;
            bool wasOpen = socketOpen;
            ReleaseSocket();
            connectionSeq++; // 남은 사건 무시

            if (ws == null)
            {
                return;
            }
            if (!wasOpen)
            {
                SafeCancel(cts);
                return;
            }
            Task.Run(async () =>
            {
                try
                {
                    using (var timeout = new CancellationTokenSource(2000))
                    {
                        await ws.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "client disconnect", timeout.Token).ConfigureAwait(false);
                    }
                }
                catch (Exception)
                {
                    // 이미 끊겼으면 상관없다.
                }
                finally
                {
                    SafeCancel(cts); // 수신 루프를 끝낸다 → finally에서 Dispose
                }
            });
        }

        /// <summary>인사 없이 바로 끊는다(앱 종료, 응답 없음).</summary>
        private void AbortSocket()
        {
            ClientWebSocket ws = socket;
            CancellationTokenSource cts = socketCts;
            ReleaseSocket();
            connectionSeq++;
            SafeCancel(cts);
            try { ws?.Abort(); } catch (Exception) { }
        }

        private void ReleaseSocket()
        {
            socket = null;
            socketCts = null;
            sendLock = null;
            socketOpen = false;
        }

        private static void SafeCancel(CancellationTokenSource cts)
        {
            if (cts == null) return;
            try { cts.Cancel(); } catch (ObjectDisposedException) { }
        }

        private static void Log(string message)
        {
            if (ApiConfig.LogRequests)
            {
                Debug.Log("[Realtime] " + message);
            }
        }
    }
}
