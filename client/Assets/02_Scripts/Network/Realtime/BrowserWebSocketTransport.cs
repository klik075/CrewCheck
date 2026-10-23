using System;

namespace WhoisntCitizen.Network.Realtime
{
    /// <summary>
    /// 브라우저 WebSocket(RealtimeWebSocket.jslib)의 함수 5개 중 "연결별로 부르는" 4개를 C#에서 본 모양.
    ///
    /// 실제 구현은 WebGL 빌드에만 있는 BrowserWebSocketNative([DllImport("__Internal")])이고,
    /// 테스트에서는 가짜 구현을 넣는다. 이렇게 나눠 두면 브라우저 없이 에디터 테스트로 동작을 확인할 수 있다.
    /// (나머지 하나인 RealtimeWS_Init은 콜백 등록이라 BrowserWebSocketNative 안에서 한 번만 부른다)
    ///
    /// jslib와의 약속
    ///  - Connect는 연결 번호(1 이상)를 돌려준다. 0이면 연결을 시작하지 못했다(Init 전).
    ///  - 번호마다 onClose 콜백이 정확히 한 번, 맨 마지막에 온다. 콜백은 이 메서드들 안에서 바로 오지 않고 항상 나중에 온다.
    /// </summary>
    public interface IBrowserWebSocketApi
    {
        /// <summary>RealtimeWS_Connect. 연결을 시작하고 번호를 돌려준다. 0이면 실패.</summary>
        int Connect(string url, int connectTimeoutMs, int maxMessageBytes);

        /// <summary>RealtimeWS_Send. 보냈으면 true, 열려 있지 않아 버렸으면 false.</summary>
        bool Send(int socketId, string text);

        /// <summary>RealtimeWS_Close. 정상 종료(1000). 열리기 전이면 연결 시도를 멈춘다.</summary>
        void Close(int socketId);

        /// <summary>RealtimeWS_Abort. 즉시 끊는다.</summary>
        void Abort(int socketId);
    }

    /// <summary>
    /// 브라우저 WebSocket으로 만든 IRealtimeTransport. WebGL 빌드에서 쓴다. (데스크톱·모바일·에디터는 ClientWebSocketTransport)
    ///
    /// 동작 흐름
    ///   Connect → router가 jslib에 연결을 부탁하고 받은 번호(socketId)로 이 객체를 기억한다.
    ///   브라우저 이벤트 → jslib 콜백(번호와 함께) → router가 번호로 이 객체를 찾아 OnOpened/OnMessage/OnClosed
    ///   → 이 객체의 RealtimeTransportInbox에 Post → RealtimeClient가 Update에서 TryDequeue로 꺼낸다.
    ///
    /// 스레드
    ///  - WebGL에는 스레드가 없다. 콜백도 메인 스레드에서 오지만 Update 바깥(브라우저 이벤트 순간)에 온다.
    ///    그래서 콜백에서 바로 게임 로직을 돌리지 않고, ClientWebSocketTransport와 똑같이 Inbox에 넣기만 한다.
    ///    RealtimeClient 입장에서는 두 구현이 완전히 같아 보인다.
    ///
    /// 데스크톱과 다른 점
    ///  - 토큰: 브라우저 WebSocket은 헤더를 붙일 수 없어 주소에 ?token= 으로 넣는다. (RealtimeProtocol.WithTokenQuery)
    ///  - 401 구분 불가: 핸드셰이크 거절은 브라우저가 1006으로만 알려 준다. 그래서 Unauthorized는 항상 false이고,
    ///    서버가 인증 실패를 "연결 후 4401로 닫기"로 알린다. (RealtimeFailurePolicy가 4401을 보고 멈춘다)
    ///  - 보내기 큐가 없다: 브라우저 send()가 부른 순서대로 보내 준다.
    ///
    /// 이 클래스는 UnityEngine·DllImport를 쓰지 않는다. 그래서 Realtime.Core에 두고 에디터 테스트로 확인한다.
    /// </summary>
    public sealed class BrowserWebSocketTransport : IRealtimeTransport
    {
        /// <summary>서버 메시지 하나의 최대 크기(바이트). ClientWebSocketTransport.MaxIncomingBytes와 같다.</summary>
        public const int MaxIncomingBytes = 64 * 1024;

        private readonly BrowserWebSocketRouter router;
        private readonly RealtimeTransportInbox inbox = new RealtimeTransportInbox();

        private int socketId;   // jslib가 준 연결 번호. 0이면 아직 연결을 시작하지 않았다
        private bool started;   // Connect를 불렀다 (약속 1: 한 번만)
        private bool opened;    // onOpen을 받았다
        private bool stopping;  // Close/Abort를 불렀다. 이후 Send는 버린다

        /// <summary>BrowserWebSocketRouter.CreateTransport()로 만든다.</summary>
        public BrowserWebSocketTransport(BrowserWebSocketRouter router)
        {
            this.router = router ?? throw new ArgumentNullException(nameof(router));
        }

        /// <summary>jslib 연결 번호. 연결 전이거나 시작하지 못했으면 0. (테스트·로그용)</summary>
        public int SocketId => socketId;

        public void Connect(string url, string accessToken, int connectTimeoutMs)
        {
            if (started)
            {
                return; // 약속 1: 객체 하나 = 연결 한 번
            }
            started = true;

            string urlWithToken;
            try
            {
                urlWithToken = RealtimeProtocol.WithTokenQuery(url, accessToken);
            }
            catch (Exception e)
            {
                // 빈 주소 등. 예외 메시지에는 토큰이 들어가지 않는다(WithTokenQuery는 주소만 검사한다).
                inbox.PostClosed(null, "connect setup failed: " + e.Message);
                return;
            }

            int id = router.Open(this, urlWithToken, Math.Max(1, connectTimeoutMs), MaxIncomingBytes, out string error);
            if (id <= 0)
            {
                // 약속 4: 예외 대신 Closed. (jslib가 빌드에 없거나 Init 전인 경우)
                inbox.PostClosed(null, error ?? "browser websocket unavailable");
                return;
            }
            socketId = id;
        }

        public void Send(string text)
        {
            // 약속 6: 열리기 전·닫는 중·닫힌 뒤에는 조용히 버린다
            if (!opened || stopping || inbox.IsClosed || text == null || socketId == 0)
            {
                return;
            }
            router.Send(socketId, text);
        }

        public void Close()
        {
            if (stopping)
            {
                return;
            }
            stopping = true;

            if (socketId == 0)
            {
                // 아직 연결을 시작하지 않았다(또는 시작하지 못했다). 이후 Connect도 막는다.
                started = true;
                inbox.PostClosed(null, "cancelled"); // 이미 Closed가 있으면 Inbox가 무시한다
                return;
            }
            // jslib가 열리기 전이면 "cancelled", 열려 있으면 서버 답(또는 2초 뒤 "close timeout")으로 onClose를 보낸다.
            router.Close(socketId);
        }

        public void Abort()
        {
            stopping = true;
            if (socketId == 0)
            {
                started = true;
                inbox.PostClosed(null, "aborted");
                return;
            }
            router.Abort(socketId); // onClose("aborted")가 나중에 온다
        }

        public bool TryDequeue(out RealtimeTransportEvent transportEvent)
        {
            return inbox.TryDequeue(out transportEvent);
        }

        // ── router가 jslib 콜백을 받아 부른다 ─────────────────────

        internal void OnOpened()
        {
            opened = true; // Opened를 꺼낸 RealtimeClient가 바로 Send할 수 있도록 먼저 표시
            inbox.PostOpened();
        }

        internal void OnMessage(string text)
        {
            inbox.PostMessage(text);
        }

        internal void OnClosed(int code, string reason)
        {
            opened = false;
            // jslib는 "종료 코드 없음"(시간 초과·취소·중단 등)을 0으로 준다. 이 프로젝트는 null로 쓴다.
            int? closeCode = code == 0 ? (int?)null : code;
            inbox.PostClosed(closeCode, string.IsNullOrEmpty(reason) ? null : reason, unauthorized: false);
        }
    }
}
