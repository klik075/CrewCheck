using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

namespace WhoisntCitizen.RoomTest
{
    // ======================================================================
    // [테스트 전용 - Assets/99_Test/RoomTest 폴더째 삭제 예정]
    // 에디터 안에서만 도는 아주 작은 HTTP 서버.
    // 요청은 백그라운드 스레드가 받아 큐에 넣고, 실제 처리는 Pump()를 부른 메인 스레드에서 한다.
    // (Inspector에서 고친 가짜 데이터를 잠금 없이 안전하게 읽기 위해)
    // 메인 코드는 이 클래스를 전혀 모른다. ApiConfig.BaseUrl만 이 서버 주소로 잠시 바꿔 둘 뿐이다.
    // ======================================================================

    /// <summary>가짜 서버가 받은 요청</summary>
    public sealed class FakeHttpRequest
    {
        public string Method;        // GET / POST / PUT / DELETE
        public string Path;          // /api/v1/rooms/9001
        public string Query;         // afterId=3&limit=50 (앞의 ? 없음)
        public string Body;          // 요청 본문 (없으면 "")
        public string Authorization; // "Bearer ..." (없으면 null)

        /// <summary>쿼리 값 하나. 없으면 null</summary>
        public string QueryValue(string key)
        {
            if (string.IsNullOrEmpty(Query)) return null;
            foreach (string pair in Query.Split('&'))
            {
                int eq = pair.IndexOf('=');
                string k = eq < 0 ? pair : pair.Substring(0, eq);
                if (k == key) return eq < 0 ? "" : Uri.UnescapeDataString(pair.Substring(eq + 1));
            }
            return null;
        }
    }

    /// <summary>가짜 서버의 응답</summary>
    public sealed class FakeHttpResponse
    {
        public int Status;
        public string Body; // null이면 본문 없음 (204 등)

        public static FakeHttpResponse Json(int status, string json)
        {
            return new FakeHttpResponse { Status = status, Body = json };
        }

        public static FakeHttpResponse NoContent()
        {
            return new FakeHttpResponse { Status = 204, Body = null };
        }

        /// <summary>실제 서버 에러 형식과 같은 {"code","message"}</summary>
        public static FakeHttpResponse Error(int status, string code, string message)
        {
            return Json(status, "{\"code\":" + FakeJson.Quote(code) + ",\"message\":" + FakeJson.Quote(message) + "}");
        }
    }

    /// <summary>JSON 문자열 이스케이프 (테스트 전용, 메인 MiniJson에 의존하지 않기 위해 따로 둔다)</summary>
    public static class FakeJson
    {
        public static string Quote(string s)
        {
            if (s == null) return "null";
            var sb = new StringBuilder(s.Length + 2);
            sb.Append('"');
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
            sb.Append('"');
            return sb.ToString();
        }
    }

    public sealed class FakeRoomServer : IDisposable
    {
        private readonly ConcurrentQueue<HttpListenerContext> pending = new ConcurrentQueue<HttpListenerContext>();
        private HttpListener listener;
        private Thread thread;
        private volatile bool running;

        /// <summary>"http://127.0.0.1:{port}" (끝에 / 없음). ApiConfig.BaseUrl에 그대로 넣는다.</summary>
        public string BaseUrl { get; private set; }

        public bool IsRunning => running;

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

            thread = new Thread(AcceptLoop) { IsBackground = true, Name = "RoomTest FakeRoomServer" };
            thread.Start();
        }

        /// <summary>쌓인 요청을 메인 스레드에서 처리한다. MonoBehaviour.Update에서 매 프레임 부른다.</summary>
        public void Pump(Func<FakeHttpRequest, FakeHttpResponse> handler)
        {
            HttpListenerContext ctx;
            while (pending.TryDequeue(out ctx))
            {
                FakeHttpResponse res;
                try
                {
                    res = handler(Read(ctx)) ?? FakeHttpResponse.Error(404, "NOT_FOUND", "[RoomTest] 응답 없음");
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                    res = FakeHttpResponse.Error(500, "FAKE_SERVER_ERROR", "[RoomTest] 가짜 서버 처리 중 예외: " + e.Message);
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

            HttpListenerContext ctx;
            while (pending.TryDequeue(out ctx))
            {
                try { ctx.Response.Abort(); } catch (Exception) { }
            }
        }

        private void AcceptLoop()
        {
            while (running)
            {
                try
                {
                    HttpListenerContext ctx = listener.GetContext();
                    pending.Enqueue(ctx);
                }
                catch (Exception)
                {
                    break; // Stop()/Close() 호출로 대기가 끊김
                }
            }
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
                Debug.LogWarning("[RoomTest] 응답 전송 실패: " + e.Message);
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
