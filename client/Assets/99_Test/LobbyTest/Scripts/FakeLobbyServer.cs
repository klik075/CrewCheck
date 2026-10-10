using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Debug = UnityEngine.Debug;

namespace WhoisntCitizen.TestScenes.LobbyTest
{
    // ======================================================================
    // [테스트 전용 - Assets/99_Test/LobbyTest 폴더째 삭제 예정]
    // 에디터 안에서만 도는 아주 작은 HTTP 서버. (RoomTest 폴더와 코드를 공유하지 않도록 따로 둔다)
    // 요청은 백그라운드 스레드가 받아 큐에 넣고, 실제 처리는 Pump()를 부른 메인 스레드에서 한다.
    // 메인 코드는 이 클래스를 전혀 모른다. ApiConfig.BaseUrl만 이 서버 주소로 잠시 바꿔 둘 뿐이다.
    // ======================================================================

    /// <summary>가짜 서버가 받은 요청</summary>
    public sealed class FakeHttpRequest
    {
        public string Method;        // GET / POST / PUT / DELETE
        public string Path;          // /api/v1/rooms/3/players
        public string Query;         // keyword=초보 (앞의 ? 없음)
        public string Body;          // 요청 본문 (없으면 "")
        public string Authorization; // "Bearer ..." (없으면 null)

        /// <summary>쿼리 값 하나 (URL 디코딩됨). 없으면 null</summary>
        public string QueryValue(string key)
        {
            if (string.IsNullOrEmpty(Query)) return null;
            foreach (string pair in Query.Split('&'))
            {
                int eq = pair.IndexOf('=');
                string k = eq < 0 ? pair : pair.Substring(0, eq);
                if (k == key) return eq < 0 ? "" : Uri.UnescapeDataString(pair.Substring(eq + 1).Replace('+', ' '));
            }
            return null;
        }
    }

    /// <summary>가짜 서버의 응답</summary>
    public sealed class FakeHttpResponse
    {
        public int Status;
        public string Body; // null이면 본문 없음 (204, 401 등)

        public static FakeHttpResponse Json(int status, string json)
        {
            return new FakeHttpResponse { Status = status, Body = json };
        }

        /// <summary>실제 서버 에러 형식과 같은 {"code","message"}</summary>
        public static FakeHttpResponse Error(int status, string code, string message)
        {
            return Json(status, "{\"code\":" + Quote(code) + ",\"message\":" + Quote(message) + "}");
        }

        public static string Quote(string s)
        {
            if (s == null) return "null";
            var sb = new StringBuilder(s.Length + 2).Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            return sb.Append('"').ToString();
        }
    }

    public sealed class FakeLobbyServer : IDisposable
    {
        private struct Pending
        {
            public HttpListenerContext Context;
            public long ReceivedTicks;
        }

        private readonly ConcurrentQueue<Pending> queue = new ConcurrentQueue<Pending>();
        private Pending? held; // 응답 지연 중인 맨 앞 요청 (순서를 지키기 위해 하나씩 처리)
        private HttpListener listener;
        private Thread thread;
        private volatile bool running;

        /// <summary>"http://127.0.0.1:{port}" (끝에 / 없음). ApiConfig.BaseUrl에 그대로 넣는다.</summary>
        public string BaseUrl { get; private set; }

        /// <param name="port">0이면 비어 있는 포트를 자동으로 고른다.</param>
        public void Start(int port = 0)
        {
            if (running) return;
            if (port <= 0) port = FindFreePort();

            listener = new HttpListener();
            listener.Prefixes.Add("http://127.0.0.1:" + port + "/");
            listener.Start();
            BaseUrl = "http://127.0.0.1:" + port;
            running = true;

            thread = new Thread(AcceptLoop) { IsBackground = true, Name = "LobbyTest FakeLobbyServer" };
            thread.Start();
        }

        /// <summary>
        /// 쌓인 요청을 메인 스레드에서 처리한다. MonoBehaviour.Update에서 매 프레임 부른다.
        /// delaySeconds만큼 늦게 응답한다. ("불러오는 중..." 같은 로딩 상태 확인용)
        /// </summary>
        public void Pump(Func<FakeHttpRequest, FakeHttpResponse> handler, float delaySeconds)
        {
            long delayTicks = (long)(Math.Max(0f, delaySeconds) * Stopwatch.Frequency);
            while (true)
            {
                if (held == null)
                {
                    if (!queue.TryDequeue(out Pending next)) return;
                    held = next;
                }
                if (Stopwatch.GetTimestamp() - held.Value.ReceivedTicks < delayTicks) return;

                HttpListenerContext ctx = held.Value.Context;
                held = null;

                FakeHttpResponse res;
                try
                {
                    res = handler(Read(ctx)) ?? FakeHttpResponse.Error(404, "NOT_FOUND", "[LobbyTest] 응답 없음");
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                    res = FakeHttpResponse.Error(500, "FAKE_SERVER_ERROR", "[LobbyTest] 가짜 서버 처리 중 예외: " + e.Message);
                }
                Write(ctx, res);
            }
        }

        public void Dispose()
        {
            running = false;
            try
            {
                if (listener != null)
                {
                    listener.Stop();
                    listener.Close();
                }
            }
            catch (Exception) { /* 종료 중 오류는 무시 */ }
            listener = null;

            if (held != null) Abort(held.Value.Context);
            held = null;
            while (queue.TryDequeue(out Pending p)) Abort(p.Context);
        }

        private void AcceptLoop()
        {
            while (running)
            {
                try
                {
                    HttpListenerContext ctx = listener.GetContext();
                    queue.Enqueue(new Pending { Context = ctx, ReceivedTicks = Stopwatch.GetTimestamp() });
                }
                catch (Exception)
                {
                    break; // Stop()/Close() 호출로 대기가 끊김
                }
            }
        }

        private static void Abort(HttpListenerContext ctx)
        {
            try { ctx.Response.Abort(); } catch (Exception) { }
        }

        private static FakeHttpRequest Read(HttpListenerContext ctx)
        {
            HttpListenerRequest r = ctx.Request;
            string body = "";
            if (r.HasEntityBody)
            {
                using (var reader = new StreamReader(r.InputStream, Encoding.UTF8))
                    body = reader.ReadToEnd();
            }
            return new FakeHttpRequest
            {
                Method = r.HttpMethod,
                Path = r.Url.AbsolutePath,
                Query = r.Url.Query.TrimStart('?'),
                Body = body,
                Authorization = r.Headers["Authorization"],
            };
        }

        private static void Write(HttpListenerContext ctx, FakeHttpResponse res)
        {
            try
            {
                HttpListenerResponse r = ctx.Response;
                r.StatusCode = res.Status;
                if (res.Body != null)
                {
                    byte[] bytes = Encoding.UTF8.GetBytes(res.Body);
                    r.ContentType = "application/json; charset=utf-8";
                    r.ContentLength64 = bytes.Length;
                    r.OutputStream.Write(bytes, 0, bytes.Length);
                }
                r.Close();
            }
            catch (Exception e)
            {
                Debug.LogWarning("[LobbyTest] 응답 전송 실패: " + e.Message);
            }
        }

        private static int FindFreePort()
        {
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            int port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            return port;
        }
    }
}
