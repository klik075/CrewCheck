// WebGL 플레이어 빌드에서만 컴파일된다. 에디터·데스크톱·모바일에는 이 파일의 내용이 없다.
// (DllImport("__Internal")의 jslib 함수는 WebGL 빌드 결과물 안에만 있기 때문)
#if UNITY_WEBGL && !UNITY_EDITOR
using System;
using System.Runtime.InteropServices;
using AOT;
using UnityEngine;
using WhoisntCitizen.Network.Realtime;

namespace WhoisntCitizen.Network
{
    /// <summary>
    /// RealtimeWebSocket.jslib와 C#을 잇는 얇은 층. 하는 일은 두 가지뿐이다.
    ///  1. C# → JS: jslib 함수 5개를 [DllImport("__Internal")]로 선언하고 IBrowserWebSocketApi로 감싼다.
    ///  2. JS → C#: jslib가 부를 static 콜백 3개를 [MonoPInvokeCallback]으로 만들고, 받은 것을 Router에 넘긴다.
    /// 판단(번호로 찾기, Inbox에 넣기, 코드 0 → null 등)은 모두 Realtime.Core의 Router·Transport가 한다.
    /// </summary>
    public sealed class BrowserWebSocketNative : IBrowserWebSocketApi
    {
        // ── JS가 부를 C# 함수의 모양 (jslib의 makeDynCall 'vi' / 'vii' / 'viii'와 짝) ──
        private delegate void OpenCallback(int socketId);
        private delegate void MessageCallback(int socketId, IntPtr textUtf8);
        private delegate void CloseCallback(int socketId, int code, IntPtr reasonUtf8);

        // ── jslib 함수 선언. 이름·인자 순서가 jslib와 정확히 같아야 한다 ──
        // string 인자는 IL2CPP가 UTF-8로 바꿔 메모리에 잠깐 놓고 포인터를 넘긴다. JS는 UTF8ToString으로 바로 읽는다.
        [DllImport("__Internal")] private static extern void RealtimeWS_Init(OpenCallback onOpen, MessageCallback onMessage, CloseCallback onClose);
        [DllImport("__Internal")] private static extern int RealtimeWS_Connect(string url, int connectTimeoutMs, int maxMessageBytes);
        [DllImport("__Internal")] private static extern int RealtimeWS_Send(int socketId, string text);
        [DllImport("__Internal")] private static extern void RealtimeWS_Close(int socketId);
        [DllImport("__Internal")] private static extern void RealtimeWS_Abort(int socketId);

        private static BrowserWebSocketRouter router;

        /// <summary>앱 전체에서 하나. 처음 쓸 때 jslib에 콜백을 등록한다.</summary>
        public static BrowserWebSocketRouter Router
        {
            get
            {
                if (router == null)
                {
                    RealtimeWS_Init(OnOpen, OnMessage, OnClose);
                    router = new BrowserWebSocketRouter(new BrowserWebSocketNative());
                }
                return router;
            }
        }

        /// <summary>RealtimeClient.CreateTransport()가 WebGL에서 부른다. (W-3-3)</summary>
        public static IRealtimeTransport CreateTransport()
        {
            return Router.CreateTransport();
        }

        private BrowserWebSocketNative()
        {
        }

        // ── IBrowserWebSocketApi: Router가 부른다 ──

        public int Connect(string url, int connectTimeoutMs, int maxMessageBytes)
        {
            return RealtimeWS_Connect(url, connectTimeoutMs, maxMessageBytes);
        }

        public bool Send(int socketId, string text)
        {
            return RealtimeWS_Send(socketId, text) == 1;
        }

        public void Close(int socketId)
        {
            RealtimeWS_Close(socketId);
        }

        public void Abort(int socketId)
        {
            RealtimeWS_Abort(socketId);
        }

        // ── jslib → C# 콜백 ──
        // IL2CPP는 미리 만들어 둔 static 함수만 JS에 넘길 수 있다. [MonoPInvokeCallback]이 "이 함수를 밖에서 부를 수 있게
        // 준비해 달라"는 표시다. 예외가 JS까지 새어 나가지 않게 모두 잡아서 로그만 남긴다.

        [MonoPInvokeCallback(typeof(OpenCallback))]
        private static void OnOpen(int socketId)
        {
            try
            {
                router?.HandleOpen(socketId);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        [MonoPInvokeCallback(typeof(MessageCallback))]
        private static void OnMessage(int socketId, IntPtr textUtf8)
        {
            try
            {
                // JS가 빌려 준 메모리의 UTF-8 글자를 C# string으로 복사한다. 콜백이 끝나면 JS가 그 메모리를 해제한다.
                string text = Marshal.PtrToStringUTF8(textUtf8) ?? string.Empty;
                router?.HandleMessage(socketId, text);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        [MonoPInvokeCallback(typeof(CloseCallback))]
        private static void OnClose(int socketId, int code, IntPtr reasonUtf8)
        {
            try
            {
                string reason = Marshal.PtrToStringUTF8(reasonUtf8);
                router?.HandleClose(socketId, code, reason);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }
    }
}
#endif
