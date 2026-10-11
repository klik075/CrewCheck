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
    /// 쓰는 쪽: RealtimeClient.Instance.Connect() / Disconnect(), StateChanged / MessageReceived 이벤트.
    /// (방 입장·퇴장, 재동기화 이벤트, 종료 코드 처리는 4-8-6 ~ 4-8-8에서 붙인다)
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

        /// <summary>연결 상태와 구독 중인 방. 화면은 읽기만 한다.</summary>
        public RealtimeConnectionTracker Tracker => tracker;

        public RealtimeState State => tracker.State;

        /// <summary>상태가 바뀔 때 (이전, 이후). 메인 스레드에서 불린다.</summary>
        public event Action<RealtimeState, RealtimeState> StateChanged;

        /// <summary>서버 메시지를 받을 때마다(해석에 성공한 것만). 메인 스레드에서 불린다.</summary>
        public event Action<RealtimeServerMessage> MessageReceived;

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

            double now = Now;

            if (wantConnected && socket == null && tracker.State == RealtimeState.Disconnected && now >= nextConnectAt)
            {
                StartConnect();
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
            backoff.Reset();
            nextConnectAt = 0; // 다음 Update에서 바로
        }

        /// <summary>연결을 끊고 다시 연결하지 않는다. 로그아웃·타이틀 복귀 때 부른다.</summary>
        public void Disconnect()
        {
            wantConnected = false;
            CloseSocketGracefully();
            if (tracker.State != RealtimeState.Disconnected)
            {
                tracker.OnDisconnected();
            }
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
            try
            {
                using (var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct))
                {
                    connectCts.CancelAfter(connectTimeoutMs);
                    await ws.ConnectAsync(uri, connectCts.Token).ConfigureAwait(false);
                }
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
            }
            finally
            {
                if (closeCode == null && ws.CloseStatus.HasValue)
                {
                    closeCode = (int)ws.CloseStatus.Value;
                    reason = reason ?? ws.CloseStatusDescription;
                }
                inbox.Enqueue(new SocketEvent { Kind = SocketEventKind.Closed, Seq = seq, CloseCode = closeCode, Reason = reason });
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
                        HandleClosed(e.CloseCode, e.Reason);
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
                    bool first = tracker.OnSubscribed(subscribed.RoomId);
                    Log($"subscribed: room={subscribed.RoomId}" + (first ? "" : " (무시/중복)"));
                    break;

                case RealtimeErrorMessage err:
                    Debug.LogWarning($"[Realtime] 서버 오류: {err.Code} {err.Message}");
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

        private void HandleClosed(int? closeCode, string reason)
        {
            socketOpen = false;
            tracker.OnDisconnected();

            if (!wantConnected)
            {
                Log($"연결 종료 (code={closeCode?.ToString() ?? "-"}, {reason})");
                return;
            }

            double delay = backoff.NextDelaySeconds();
            nextConnectAt = Now + delay;
            Log($"연결 끊김 (code={closeCode?.ToString() ?? "-"}, {reason}) → {delay:0.0}초 뒤 다시 연결");
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
