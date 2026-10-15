using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using WhoisntCitizen.Network.Realtime;

namespace WhoisntCitizen.Network
{
    /// <summary>
    /// .NET ClientWebSocket으로 만든 IRealtimeTransport. 데스크톱·모바일·에디터에서 쓴다. (WebGL은 W-3 BrowserWebSocketTransport)
    /// RealtimeClient 안에 있던 소켓 코드(RunSocketAsync·Send·정상 종료·Abort)를 그대로 옮겨 왔다. 동작은 같다.
    ///
    /// 스레드
    ///  - 연결·받기: 백그라운드 스레드(Task.Run)에서 돈다. 받은 것은 RealtimeTransportInbox에 Post만 한다.
    ///  - 보내기: Send는 보낼 글을 큐에 넣기만 한다. 보내기 일꾼 하나(백그라운드)가 큐를 차례로 비우며 SendAsync한다.
    ///          ClientWebSocket은 동시에 보내기를 하나만 허용하고, 부른 순서대로 보내야 하므로 일꾼을 하나만 둔다.
    ///  - 메서드(Connect·Send·Close·Abort·TryDequeue)는 메인 스레드에서 부른다. 기다리지 않고 바로 돌아온다.
    ///  - Unity API를 전혀 쓰지 않는다. (백그라운드 스레드에서 Unity API를 부르면 오류가 난다)
    ///
    /// 토큰은 Authorization: Bearer 헤더로 보낸다. 연결 유지는 앱 수준 ping(10초, RealtimeClient)으로 하므로
    /// ClientWebSocket 자체의 keep-alive는 끈다. (서버는 텍스트 메시지만 "살아 있음"으로 센다)
    /// </summary>
    public sealed class ClientWebSocketTransport : IRealtimeTransport
    {
        /// <summary>서버 메시지 하나의 최대 크기. 서버 메시지는 수십 바이트라 넉넉하다. 넘으면 이상한 연결로 보고 끊는다.</summary>
        public const int MaxIncomingBytes = 64 * 1024;

        /// <summary>한 번에 받는 조각 크기. 메시지가 이보다 크면 여러 조각으로 나눠 받아 이어 붙인다.</summary>
        private const int ReceiveBufferBytes = 4096;

        /// <summary>정상 종료(Close)할 때 서버 답을 기다리는 최대 시간.</summary>
        private const int CloseTimeoutMs = 2000;

        private readonly RealtimeTransportInbox inbox = new RealtimeTransportInbox();
        private readonly CancellationTokenSource cts = new CancellationTokenSource();
        private readonly ConcurrentQueue<byte[]> sendQueue = new ConcurrentQueue<byte[]>();
        private int sending;            // 보내기 일꾼이 돌고 있으면 1 (Interlocked)

        private ClientWebSocket socket;
        private int started;            // Connect를 불렀으면 1 (Interlocked로 한 번만)
        private volatile bool opened;   // 핸드셰이크 성공 후 true. 받기 스레드가 쓰고 메인 스레드가 읽는다
        private volatile bool stopping; // Close/Abort를 불렀으면 true. 이후 Send는 버린다

        public void Connect(string url, string accessToken, int connectTimeoutMs)
        {
            if (Interlocked.Exchange(ref started, 1) == 1)
            {
                return; // 약속 1: 객체 하나 = 연결 한 번
            }

            Uri uri;
            ClientWebSocket ws;
            try
            {
                uri = new Uri(url);
                ws = new ClientWebSocket();
                if (!string.IsNullOrWhiteSpace(accessToken))
                {
                    ws.Options.SetRequestHeader("Authorization", "Bearer " + accessToken);
                }
                ws.Options.KeepAliveInterval = TimeSpan.Zero;
            }
            catch (Exception e)
            {
                // 약속 4: 예외를 던지지 않고 Closed로 알린다
                inbox.PostClosed(null, "connect setup failed: " + e.Message);
                return;
            }

            socket = ws;
            CancellationToken ct = cts.Token;
            int timeoutMs = Math.Max(1, connectTimeoutMs);
            Task.Run(() => RunAsync(ws, uri, timeoutMs, ct));
        }

        /// <summary>백그라운드 스레드. 연결 → 받기 반복 → 끝나면 Closed 한 번. Unity API를 쓰지 않는다.</summary>
        private async Task RunAsync(ClientWebSocket ws, Uri uri, int connectTimeoutMs, CancellationToken ct)
        {
            int? closeCode = null;
            string reason = null;
            bool unauthorized = false;
            bool connectTimedOut = false;
            try
            {
                using (var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct))
                {
                    connectCts.CancelAfter(connectTimeoutMs);
                    try
                    {
                        await ws.ConnectAsync(uri, connectCts.Token).ConfigureAwait(false);
                    }
                    catch (Exception) when (connectCts.IsCancellationRequested && !ct.IsCancellationRequested)
                    {
                        // 런타임에 따라 시간 초과가 OperationCanceledException이 아니라
                        // WebSocketException(속은 ObjectDisposedException)으로 올 수 있어 따로 표시하고 다시 던진다.
                        connectTimedOut = true;
                        throw;
                    }
                }
                opened = true;          // Opened를 꺼낸 메인 스레드가 바로 Send할 수 있도록 먼저 표시
                inbox.PostOpened();

                var buffer = new byte[ReceiveBufferBytes];
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
                            continue; // 아직 조각이 더 온다
                        }

                        if (result.MessageType == WebSocketMessageType.Text)
                        {
                            inbox.PostMessage(Encoding.UTF8.GetString(message.GetBuffer(), 0, (int)message.Length));
                        }
                        message.SetLength(0); // 다음 메시지를 위해 비운다 (바이너리는 버린다)
                    }
                }
            }
            catch (OperationCanceledException)
            {
                reason = ct.IsCancellationRequested ? "cancelled" : "connect timeout";
            }
            catch (Exception) when (ct.IsCancellationRequested || connectTimedOut)
            {
                // Close·Abort로 멈췄거나 연결 시간이 초과돼 소켓이 정리되면서 난 예외. 원인을 알아보기 쉬운 이유로 바꾼다.
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
                opened = false;
                inbox.PostClosed(closeCode, reason, unauthorized);
                ws.Dispose();
            }
        }

        public void Send(string text)
        {
            // 약속 6: 열리기 전·닫는 중·닫힌 뒤에는 조용히 버린다
            if (!opened || stopping || inbox.IsClosed || text == null)
            {
                return;
            }
            sendQueue.Enqueue(Encoding.UTF8.GetBytes(text));
            if (Interlocked.CompareExchange(ref sending, 1, 0) == 0)
            {
                // 일꾼이 쉬고 있으면 깨운다. 이미 돌고 있으면 큐에 넣은 것을 그 일꾼이 이어서 보낸다.
                ClientWebSocket ws = socket;
                Task.Run(() => DrainSendQueueAsync(ws, cts.Token));
            }
        }

        /// <summary>
        /// 보내기 일꾼(백그라운드). 큐가 빌 때까지 넣은 순서대로 하나씩 보낸다. 한 번에 하나만 돈다(sending 깃발).
        /// 큐를 비우고 깃발을 내린 직후 다른 스레드가 넣었을 수 있으므로, 한 번 더 확인하고 다시 깃발을 올린다.
        /// </summary>
        private async Task DrainSendQueueAsync(ClientWebSocket ws, CancellationToken ct)
        {
            while (true)
            {
                while (sendQueue.TryDequeue(out byte[] bytes))
                {
                    try
                    {
                        if (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
                        {
                            await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, ct).ConfigureAwait(false);
                        }
                    }
                    catch (Exception)
                    {
                        // 소켓이 닫히는 중이면 SendAsync가 실패한다. 끊김은 받기 루프의 Closed로 알려진다.
                    }
                }
                Interlocked.Exchange(ref sending, 0);
                if (sendQueue.IsEmpty || Interlocked.CompareExchange(ref sending, 1, 0) != 0)
                {
                    return;
                }
            }
        }

        public void Close()
        {
            if (stopping)
            {
                return;
            }
            stopping = true;

            ClientWebSocket ws = socket;
            if (ws == null || !opened)
            {
                SafeCancel(); // 아직 연결 중이면 연결 시도를 멈춘다 → 받기 스레드가 Closed("cancelled")
                return;
            }

            Task.Run(async () =>
            {
                try
                {
                    using (var timeout = new CancellationTokenSource(CloseTimeoutMs))
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
                    SafeCancel(); // 받기 루프를 끝낸다 → finally에서 Dispose
                }
            });
        }

        public void Abort()
        {
            stopping = true;
            SafeCancel();
            try { socket?.Abort(); } catch (Exception) { }
        }

        public bool TryDequeue(out RealtimeTransportEvent transportEvent)
        {
            return inbox.TryDequeue(out transportEvent);
        }

        private void SafeCancel()
        {
            try { cts.Cancel(); } catch (ObjectDisposedException) { }
        }
    }
}
