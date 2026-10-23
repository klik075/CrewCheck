namespace WhoisntCitizen.Network.Realtime
{
    /// <summary>transport가 RealtimeClient에게 알려 주는 사건의 종류.</summary>
    public enum RealtimeTransportEventKind
    {
        /// <summary>WebSocket 연결(핸드셰이크)이 성공했다. 이제 보낼 수 있다.</summary>
        Opened,

        /// <summary>서버가 텍스트 메시지 하나를 보냈다. (Text)</summary>
        Message,

        /// <summary>연결이 끝났다. 연결에 실패했을 때도 이것 하나로 알린다. (CloseCode, Reason, Unauthorized)</summary>
        Closed,
    }

    /// <summary>
    /// transport(실제 소켓)에서 일어난 일 하나. RealtimeClient는 이 세 가지만 보고 동작한다.
    ///
    ///   Opened  → 연결됨. connected 메시지를 기다린다.
    ///   Message → 서버 메시지 한 개(JSON 문자열). RealtimeMessageCodec으로 해석한다.
    ///   Closed  → 끊김. CloseCode·Unauthorized를 보고 다시 연결할지 멈출지 정한다. (RealtimeFailurePolicy)
    ///
    /// 만드는 방법은 Opened() / Message(text) / Closed(...) 세 정적 메서드뿐이다. (잘못된 조합을 만들 수 없게)
    /// 한 번 만들면 바뀌지 않는다. (다른 스레드에서 만들어 메인 스레드로 넘겨도 안전하다)
    /// </summary>
    public sealed class RealtimeTransportEvent
    {
        public RealtimeTransportEventKind Kind { get; }

        /// <summary>Message일 때 받은 텍스트. 그 외에는 null.</summary>
        public string Text { get; }

        /// <summary>
        /// Closed일 때 WebSocket 종료 코드 (1000 정상, 4401 토큰 만료, 4408 무응답 등).
        /// 종료 코드 없이 끊겼으면(네트워크 오류, 연결 실패, 타임아웃) null.
        /// </summary>
        public int? CloseCode { get; }

        /// <summary>Closed일 때 종료 이유 또는 오류 메시지 (로그용). 없으면 null.</summary>
        public string Reason { get; }

        /// <summary>
        /// Closed일 때, 연결하는 순간(핸드셰이크) 서버가 HTTP 401로 거절했으면 true.
        /// 토큰이 없거나 잘못됐거나 만료됐다는 뜻이라 다시 연결해도 소용이 없다.
        /// 브라우저(WebGL)는 이 정보를 주지 않으므로 그 구현에서는 항상 false다.
        /// </summary>
        public bool Unauthorized { get; }

        private RealtimeTransportEvent(RealtimeTransportEventKind kind, string text, int? closeCode, string reason, bool unauthorized)
        {
            Kind = kind;
            Text = text;
            CloseCode = closeCode;
            Reason = reason;
            Unauthorized = unauthorized;
        }

        public static RealtimeTransportEvent Opened()
        {
            return new RealtimeTransportEvent(RealtimeTransportEventKind.Opened, null, null, null, false);
        }

        public static RealtimeTransportEvent Message(string text)
        {
            return new RealtimeTransportEvent(RealtimeTransportEventKind.Message, text ?? string.Empty, null, null, false);
        }

        public static RealtimeTransportEvent Closed(int? closeCode, string reason, bool unauthorized = false)
        {
            return new RealtimeTransportEvent(RealtimeTransportEventKind.Closed, null, closeCode, reason, unauthorized);
        }

        public override string ToString()
        {
            switch (Kind)
            {
                case RealtimeTransportEventKind.Message:
                    return "Message(" + Text + ")";
                case RealtimeTransportEventKind.Closed:
                    return "Closed(code=" + (CloseCode.HasValue ? CloseCode.Value.ToString() : "none")
                        + ", reason=" + (Reason ?? "") + (Unauthorized ? ", 401" : "") + ")";
                default:
                    return Kind.ToString();
            }
        }
    }

    /// <summary>
    /// WebSocket 연결 하나를 맡는 부품. RealtimeClient는 이 인터페이스만 알고, 실제 소켓 종류는 모른다.
    ///
    /// 구현 (W-2, W-3)
    ///  - ClientWebSocketTransport  : 데스크톱·모바일·에디터. .NET ClientWebSocket, 받기는 백그라운드 스레드.
    ///  - BrowserWebSocketTransport : WebGL 빌드. 브라우저의 JavaScript WebSocket(.jslib), 싱글 스레드.
    ///
    /// 약속 (모든 구현이 지킨다)
    ///  1. 객체 하나 = 연결 한 번. Connect는 한 번만 부른다. 다시 연결할 때는 새 객체를 만든다.
    ///     (그래서 끊긴 예전 소켓의 늦은 사건이 새 연결에 섞이지 않는다)
    ///  2. 사건은 일어난 순서대로 TryDequeue로 꺼낸다. 순서: Opened? → Message* → Closed
    ///     (연결에 실패하면 Opened 없이 Closed만 온다)
    ///  3. Closed는 정확히 한 번, 맨 마지막에 온다. 그 뒤에는 아무 사건도 없다.
    ///     단, Close()·Abort()를 부른 뒤에는 RealtimeClient가 이 객체를 버리므로 Closed를 기다리지 않아도 된다.
    ///  4. 메서드는 예외를 던지지 않는다. 실패는 Closed 사건으로 알린다.
    ///  5. 메서드는 메인 스레드에서만 부른다. 구현은 사건을 다른 스레드에서 만들 수 있으므로
    ///     사건 보관은 스레드에 안전해야 한다. (W-1-2 RealtimeTransportInbox)
    ///  6. Opened 전이나 Closed 뒤의 Send는 조용히 버린다.
    /// </summary>
    public interface IRealtimeTransport
    {
        /// <summary>
        /// 연결을 시작하고 바로 돌아온다. 결과는 사건(Opened 또는 Closed)으로 알린다.
        /// </summary>
        /// <param name="url">ws:// 또는 wss:// 주소 (RealtimeProtocol.BuildUrl).</param>
        /// <param name="accessToken">JWT. 데스크톱은 Authorization 헤더로, WebGL은 ?token= 으로 보낸다.</param>
        /// <param name="connectTimeoutMs">이 시간 안에 연결되지 않으면 Closed(null, "connect timeout").</param>
        void Connect(string url, string accessToken, int connectTimeoutMs);

        /// <summary>텍스트 메시지 하나를 보낸다. 기다리지 않고 바로 돌아온다. 보내는 순서는 부른 순서와 같다.</summary>
        void Send(string text);

        /// <summary>정상 종료(1000)를 알리고 닫는다. 로그아웃처럼 일부러 끊을 때.</summary>
        void Close();

        /// <summary>인사 없이 바로 끊는다. 응답이 없거나 앱을 끌 때.</summary>
        void Abort();

        /// <summary>쌓인 사건을 하나 꺼낸다. 없으면 false. RealtimeClient가 매 프레임 비울 때까지 부른다.</summary>
        bool TryDequeue(out RealtimeTransportEvent transportEvent);
    }
}
