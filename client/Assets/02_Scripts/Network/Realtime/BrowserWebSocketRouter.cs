using System;
using System.Collections.Generic;

namespace WhoisntCitizen.Network.Realtime
{
    /// <summary>
    /// jslib 콜백(번호만 들고 온다)을 알맞은 BrowserWebSocketTransport로 보내 주는 "교환원".
    ///
    /// 왜 필요한가
    ///  - JS가 부를 수 있는 C# 콜백은 static 메서드뿐이다(IL2CPP 제약, BrowserWebSocketNative 참고).
    ///    static 메서드는 "어느 transport 객체의 일인지" 모른다.
    ///  - 그래서 연결할 때 jslib가 준 번호(socketId)와 transport를 표(Dictionary)에 적어 두고,
    ///    콜백이 오면 번호로 찾아서 그 transport에게 넘긴다.
    ///
    ///   jslib onMessage(7, "...") → BrowserWebSocketNative.OnMessage(static) → router.HandleMessage(7, "...")
    ///   → transports[7].OnMessage("...") → 그 transport의 Inbox에 Post
    ///
    /// 표 정리
    ///  - onClose가 오면 표에서 지운다. jslib는 번호마다 onClose를 반드시 한 번 보내므로 표에 찌꺼기가 남지 않는다.
    ///  - 표에 없는 번호의 콜백은 무시한다. (이미 끝난 연결)
    ///
    /// 스레드: WebGL은 싱글 스레드라 lock이 없다. 메인 스레드에서만 쓴다.
    /// jslib 쪽 함수가 예외를 던져도(빌드에 jslib가 빠진 경우 등) 바깥으로 내보내지 않는다. (IRealtimeTransport 약속 4)
    /// </summary>
    public sealed class BrowserWebSocketRouter
    {
        private readonly IBrowserWebSocketApi api;
        private readonly Dictionary<int, BrowserWebSocketTransport> transports = new Dictionary<int, BrowserWebSocketTransport>();

        public BrowserWebSocketRouter(IBrowserWebSocketApi api)
        {
            this.api = api ?? throw new ArgumentNullException(nameof(api));
        }

        /// <summary>아직 onClose를 받지 않은 연결 수. (테스트·진단용)</summary>
        public int ActiveCount => transports.Count;

        /// <summary>연결 한 번에 쓸 새 transport. RealtimeClient.CreateTransport()가 WebGL에서 부른다. (W-3-3)</summary>
        public BrowserWebSocketTransport CreateTransport()
        {
            return new BrowserWebSocketTransport(this);
        }

        // ── transport가 부른다 ───────────────────────────────────

        /// <summary>연결을 시작하고 번호로 transport를 기억한다. 실패하면 0과 이유를 돌려준다.</summary>
        internal int Open(BrowserWebSocketTransport transport, string url, int connectTimeoutMs, int maxMessageBytes, out string error)
        {
            error = null;
            int id;
            try
            {
                id = api.Connect(url, connectTimeoutMs, maxMessageBytes);
            }
            catch (Exception e)
            {
                // 예외 메시지에는 주소(=토큰)가 들어갈 수 있으므로 종류 이름만 남긴다.
                error = "browser websocket unavailable: " + e.GetType().Name;
                return 0;
            }

            if (id <= 0)
            {
                error = "browser websocket unavailable";
                return 0;
            }
            transports[id] = transport;
            return id;
        }

        internal void Send(int socketId, string text)
        {
            try
            {
                api.Send(socketId, text);
            }
            catch (Exception)
            {
                // 보내기 실패는 끊김으로 이어지고, 끊김은 onClose로 알려진다.
            }
        }

        internal void Close(int socketId)
        {
            try
            {
                api.Close(socketId);
            }
            catch (Exception e)
            {
                // jslib가 onClose를 보내 주지 못하는 상황이다. 여기서 직접 끝낸다.
                FinishLocally(socketId, "close failed: " + e.GetType().Name);
            }
        }

        internal void Abort(int socketId)
        {
            try
            {
                api.Abort(socketId);
            }
            catch (Exception e)
            {
                FinishLocally(socketId, "abort failed: " + e.GetType().Name);
            }
        }

        private void FinishLocally(int socketId, string reason)
        {
            if (transports.TryGetValue(socketId, out BrowserWebSocketTransport transport))
            {
                transports.Remove(socketId);
                transport.OnClosed(0, reason);
            }
        }

        // ── jslib 콜백이 부른다 (BrowserWebSocketNative의 static 콜백 → 여기) ──

        public void HandleOpen(int socketId)
        {
            if (transports.TryGetValue(socketId, out BrowserWebSocketTransport transport))
            {
                transport.OnOpened();
            }
        }

        public void HandleMessage(int socketId, string text)
        {
            if (transports.TryGetValue(socketId, out BrowserWebSocketTransport transport))
            {
                transport.OnMessage(text);
            }
        }

        /// <param name="code">종료 코드. 0이면 코드 없음(시간 초과·취소·중단 등).</param>
        public void HandleClose(int socketId, int code, string reason)
        {
            if (transports.TryGetValue(socketId, out BrowserWebSocketTransport transport))
            {
                transports.Remove(socketId); // 먼저 지운다: 이 번호로 다시 오는 콜백은 무시된다
                transport.OnClosed(code, reason);
            }
        }
    }
}
