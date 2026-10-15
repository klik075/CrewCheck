using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace WhoisntCitizen.Network.Realtime.Tests
{
    /// <summary>
    /// BrowserWebSocketTransport + BrowserWebSocketRouter를 가짜 jslib(FakeBrowserApi)로 확인한다.
    /// 브라우저 콜백은 테스트가 router.HandleOpen/HandleMessage/HandleClose를 직접 불러 흉내 낸다.
    /// </summary>
    public class BrowserWebSocketTransportTests
    {
        /// <summary>jslib 대신 쓰는 가짜. 불린 내용을 기록하고, 번호를 1, 2, 3… 차례로 준다.</summary>
        private sealed class FakeBrowserApi : IBrowserWebSocketApi
        {
            public readonly List<string> Calls = new List<string>();
            public string LastUrl;
            public int LastTimeoutMs;
            public int LastMaxBytes;
            public int NextId = 1;
            public bool ReturnZero;
            public bool ThrowOnConnect;
            public bool ThrowOnClose;

            public int Connect(string url, int connectTimeoutMs, int maxMessageBytes)
            {
                if (ThrowOnConnect) throw new EntryPointNotFoundException("RealtimeWS_Connect " + url);
                LastUrl = url;
                LastTimeoutMs = connectTimeoutMs;
                LastMaxBytes = maxMessageBytes;
                if (ReturnZero) { Calls.Add("Connect->0"); return 0; }
                int id = NextId++;
                Calls.Add("Connect->" + id);
                return id;
            }

            public bool Send(int socketId, string text) { Calls.Add("Send " + socketId + " " + text); return true; }

            public void Close(int socketId)
            {
                if (ThrowOnClose) throw new InvalidOperationException("boom");
                Calls.Add("Close " + socketId);
            }

            public void Abort(int socketId) { Calls.Add("Abort " + socketId); }
        }

        private FakeBrowserApi api;
        private BrowserWebSocketRouter router;

        [SetUp]
        public void SetUp()
        {
            api = new FakeBrowserApi();
            router = new BrowserWebSocketRouter(api);
        }

        private static List<string> Drain(IRealtimeTransport t)
        {
            var list = new List<string>();
            while (t.TryDequeue(out RealtimeTransportEvent e))
            {
                list.Add(e.ToString());
            }
            return list;
        }

        private BrowserWebSocketTransport Connected(string token = "tok")
        {
            BrowserWebSocketTransport t = router.CreateTransport();
            t.Connect("ws://host/ws/v1", token, 5000);
            return t;
        }

        // ------------------------------------------------------------ 연결

        [Test]
        public void Connect는_토큰을_주소에_넣고_시간과_최대크기를_넘긴다()
        {
            BrowserWebSocketTransport t = Connected("abc.def");
            Assert.AreEqual("ws://host/ws/v1?token=abc.def", api.LastUrl);
            Assert.AreEqual(5000, api.LastTimeoutMs);
            Assert.AreEqual(BrowserWebSocketTransport.MaxIncomingBytes, api.LastMaxBytes);
            Assert.AreEqual(1, t.SocketId);
            Assert.AreEqual(1, router.ActiveCount);
            CollectionAssert.IsEmpty(Drain(t), "결과는 콜백으로 나중에 온다");
        }

        [Test]
        public void 토큰이_비면_주소를_그대로_쓴다()
        {
            Connected("");
            Assert.AreEqual("ws://host/ws/v1", api.LastUrl);
        }

        [Test]
        public void 시간이_0이하면_1로_넘긴다()
        {
            router.CreateTransport().Connect("ws://host/ws/v1", "t", 0);
            Assert.AreEqual(1, api.LastTimeoutMs);
        }

        [Test]
        public void Connect를_두번_불러도_한번만_연결한다()
        {
            BrowserWebSocketTransport t = Connected();
            t.Connect("ws://host/ws/v1", "tok", 5000);
            CollectionAssert.AreEqual(new[] { "Connect->1" }, api.Calls);
        }

        [Test]
        public void 정상_흐름은_Opened_Message_Closed_순서다()
        {
            BrowserWebSocketTransport t = Connected();
            router.HandleOpen(1);
            router.HandleMessage(1, "{\"type\":\"connected\"}");
            router.HandleMessage(1, "한글");
            router.HandleClose(1, 1000, "bye");
            CollectionAssert.AreEqual(
                new[] { "Opened", "Message({\"type\":\"connected\"})", "Message(한글)", "Closed(code=1000, reason=bye)" },
                Drain(t));
            Assert.AreEqual(0, router.ActiveCount, "onClose가 오면 표에서 지운다");
        }

        [Test]
        public void 연결_두개의_사건이_섞이지_않는다()
        {
            BrowserWebSocketTransport a = Connected();
            BrowserWebSocketTransport b = Connected();
            router.HandleOpen(2);
            router.HandleOpen(1);
            router.HandleMessage(1, "a1");
            router.HandleMessage(2, "b1");
            router.HandleClose(1, 1006, "connection closed");
            CollectionAssert.AreEqual(new[] { "Opened", "Message(a1)", "Closed(code=1006, reason=connection closed)" }, Drain(a));
            CollectionAssert.AreEqual(new[] { "Opened", "Message(b1)" }, Drain(b));
            Assert.AreEqual(1, router.ActiveCount);
        }

        // ------------------------------------------------------------ 종료 코드

        [Test]
        public void 코드_0은_null로_바꾼다()
        {
            BrowserWebSocketTransport t = Connected();
            router.HandleClose(1, 0, "connect timeout");
            Assert.IsTrue(t.TryDequeue(out RealtimeTransportEvent e));
            Assert.IsNull(e.CloseCode);
            Assert.AreEqual("connect timeout", e.Reason);
        }

        [Test]
        public void 서버의_4401은_그대로_전달되고_정책이_멈춤으로_본다()
        {
            BrowserWebSocketTransport t = Connected();
            router.HandleOpen(1);
            router.HandleClose(1, RealtimeProtocol.CloseTokenExpired, "unauthorized");
            Assert.IsTrue(t.TryDequeue(out RealtimeTransportEvent opened));
            Assert.AreEqual(RealtimeTransportEventKind.Opened, opened.Kind);
            Assert.IsTrue(t.TryDequeue(out RealtimeTransportEvent e));
            Assert.AreEqual(4401, e.CloseCode);
            Assert.IsFalse(e.Unauthorized, "브라우저는 401을 알 수 없어 항상 false");
            Assert.AreEqual(CloseAction.StopAuthExpired, RealtimeFailurePolicy.ForClose(e.CloseCode, e.Unauthorized));
        }

        [Test]
        public void 빈_이유는_null이다()
        {
            BrowserWebSocketTransport t = Connected();
            router.HandleClose(1, 1000, "");
            Assert.IsTrue(t.TryDequeue(out RealtimeTransportEvent e));
            Assert.IsNull(e.Reason);
        }

        // ------------------------------------------------------------ 이상한 콜백

        [Test]
        public void 모르는_번호의_콜백은_무시한다()
        {
            BrowserWebSocketTransport t = Connected();
            router.HandleOpen(99);
            router.HandleMessage(99, "x");
            router.HandleClose(99, 1000, "x");
            CollectionAssert.IsEmpty(Drain(t));
            Assert.AreEqual(1, router.ActiveCount);
        }

        [Test]
        public void Closed_뒤의_콜백은_무시한다()
        {
            BrowserWebSocketTransport t = Connected();
            router.HandleClose(1, 1006, "connect failed");
            router.HandleOpen(1);
            router.HandleMessage(1, "late");
            router.HandleClose(1, 1000, "again");
            CollectionAssert.AreEqual(new[] { "Closed(code=1006, reason=connect failed)" }, Drain(t));
        }

        // ------------------------------------------------------------ 연결 실패

        [Test]
        public void 번호_0이면_바로_Closed이고_표에_넣지_않는다()
        {
            api.ReturnZero = true;
            BrowserWebSocketTransport t = Connected();
            CollectionAssert.AreEqual(new[] { "Closed(code=none, reason=browser websocket unavailable)" }, Drain(t));
            Assert.AreEqual(0, t.SocketId);
            Assert.AreEqual(0, router.ActiveCount);
        }

        [Test]
        public void jslib가_예외를_던져도_밖으로_안나가고_토큰을_남기지_않는다()
        {
            api.ThrowOnConnect = true;
            BrowserWebSocketTransport t = router.CreateTransport();
            Assert.DoesNotThrow(() => t.Connect("ws://host/ws/v1", "SECRET", 5000));
            List<string> events = Drain(t);
            Assert.AreEqual(1, events.Count);
            StringAssert.Contains("EntryPointNotFoundException", events[0]);
            StringAssert.DoesNotContain("SECRET", events[0]);
        }

        [Test]
        public void 빈_주소면_jslib를_부르지_않고_Closed()
        {
            BrowserWebSocketTransport t = router.CreateTransport();
            t.Connect("  ", "tok", 5000);
            CollectionAssert.IsEmpty(api.Calls);
            List<string> events = Drain(t);
            Assert.AreEqual(1, events.Count);
            StringAssert.StartsWith("Closed(code=none, reason=connect setup failed", events[0]);
        }

        // ------------------------------------------------------------ 보내기

        [Test]
        public void 열리기_전_Send는_버린다()
        {
            BrowserWebSocketTransport t = Connected();
            t.Send("early");
            CollectionAssert.AreEqual(new[] { "Connect->1" }, api.Calls);
        }

        [Test]
        public void 열린_뒤_Send는_순서대로_jslib에_넘긴다()
        {
            BrowserWebSocketTransport t = Connected();
            router.HandleOpen(1);
            t.Send("a");
            t.Send("b");
            t.Send(null);
            CollectionAssert.AreEqual(new[] { "Connect->1", "Send 1 a", "Send 1 b" }, api.Calls);
        }

        [Test]
        public void Close나_Closed_뒤_Send는_버린다()
        {
            BrowserWebSocketTransport t = Connected();
            router.HandleOpen(1);
            t.Close();
            t.Send("after close");

            BrowserWebSocketTransport t2 = Connected();
            router.HandleOpen(2);
            router.HandleClose(2, 1000, "bye");
            t2.Send("after closed");

            CollectionAssert.AreEqual(new[] { "Connect->1", "Close 1", "Connect->2" }, api.Calls);
        }

        // ------------------------------------------------------------ 닫기

        [Test]
        public void Close는_한번만_jslib에_전달하고_결과는_콜백으로_온다()
        {
            BrowserWebSocketTransport t = Connected();
            router.HandleOpen(1);
            t.Close();
            t.Close();
            CollectionAssert.AreEqual(new[] { "Connect->1", "Close 1" }, api.Calls);
            router.HandleClose(1, 1000, "client disconnect");
            CollectionAssert.AreEqual(new[] { "Opened", "Closed(code=1000, reason=client disconnect)" }, Drain(t));
            Assert.AreEqual(0, router.ActiveCount);
        }

        [Test]
        public void Connect_전_Close는_cancelled이고_이후_Connect는_무시한다()
        {
            BrowserWebSocketTransport t = router.CreateTransport();
            t.Close();
            t.Connect("ws://host/ws/v1", "tok", 5000);
            CollectionAssert.IsEmpty(api.Calls);
            CollectionAssert.AreEqual(new[] { "Closed(code=none, reason=cancelled)" }, Drain(t));
        }

        [Test]
        public void Abort는_jslib에_전달하고_Send를_막는다()
        {
            BrowserWebSocketTransport t = Connected();
            router.HandleOpen(1);
            t.Abort();
            t.Send("x");
            CollectionAssert.AreEqual(new[] { "Connect->1", "Abort 1" }, api.Calls);
            router.HandleClose(1, 0, "aborted");
            CollectionAssert.AreEqual(new[] { "Opened", "Closed(code=none, reason=aborted)" }, Drain(t));
        }

        [Test]
        public void Close가_실패하면_직접_Closed를_만들고_표에서_지운다()
        {
            BrowserWebSocketTransport t = Connected();
            api.ThrowOnClose = true;
            Assert.DoesNotThrow(() => t.Close());
            CollectionAssert.AreEqual(new[] { "Closed(code=none, reason=close failed: InvalidOperationException)" }, Drain(t));
            Assert.AreEqual(0, router.ActiveCount);
        }

        [Test]
        public void IRealtimeTransport로_쓸_수_있다()
        {
            IRealtimeTransport t = router.CreateTransport();
            Assert.IsInstanceOf<BrowserWebSocketTransport>(t);
        }
    }
}
