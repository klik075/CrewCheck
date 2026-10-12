using System;

namespace WhoisntCitizen.Network.Realtime
{
    /// <summary>
    /// 서버 WebSocket(v1) 약속값을 모아 둔 클래스.
    /// 서버 RealtimeProtocol / ServerMessage / ClientMessage / RealtimeErrorCode 와 값이 같아야 한다.
    /// (문서: 서버 저장소 Claude outputs/websocket-protocol.md)
    /// </summary>
    public static class RealtimeProtocol
    {
        /// <summary>WebSocket 접속 경로. REST 주소(BaseUrl) 뒤에 붙는다.</summary>
        public const string Path = "/ws/v1";

        /// <summary>클라이언트가 ping을 보내는 주기(초). 서버 connected 메시지의 heartbeatSeconds와 같다.</summary>
        public const int HeartbeatIntervalSeconds = 10;

        /// <summary>이 시간(초) 동안 아무 메시지도 없으면 서버가 연결을 끊는다(4408).</summary>
        public const int IdleTimeoutSeconds = 30;

        /// <summary>클라이언트가 보낼 수 있는 메시지 최대 길이(문자).</summary>
        public const int MaxClientMessageChars = 4096;

        /// <summary>서버가 연결을 닫을 때 쓰는 코드: 너무 오래 조용함.</summary>
        public const int CloseIdleTimeout = 4408;

        /// <summary>서버가 연결을 닫을 때 쓰는 코드: 토큰 만료. 다시 로그인해야 한다.</summary>
        public const int CloseTokenExpired = 4401;

        /// <summary>
        /// REST 주소를 WebSocket 주소로 바꾼다.
        /// http://host:8080 → ws://host:8080/ws/v1, https://host → wss://host/ws/v1
        /// </summary>
        public static string BuildUrl(string baseUrl)
        {
            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                throw new ArgumentException("baseUrl이 비어 있습니다.", nameof(baseUrl));
            }

            string url = baseUrl.Trim().TrimEnd('/');
            if (url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                url = "wss://" + url.Substring("https://".Length);
            }
            else if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            {
                url = "ws://" + url.Substring("http://".Length);
            }
            else if (!url.StartsWith("ws://", StringComparison.OrdinalIgnoreCase)
                     && !url.StartsWith("wss://", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("http(s):// 또는 ws(s):// 주소여야 합니다: " + baseUrl, nameof(baseUrl));
            }
            return url + Path;
        }
    }

    /// <summary>서버 → 클라이언트 메시지 type 값.</summary>
    public static class ServerMessageTypes
    {
        public const string Connected = "connected";
        public const string Pong = "pong";
        public const string Subscribed = "subscribed";
        public const string GameChanged = "game.changed";
        public const string RoomChanged = "room.changed";
        public const string ChatChanged = "chat.changed";
        public const string Error = "error";
    }

    /// <summary>클라이언트 → 서버 메시지 type 값.</summary>
    public static class ClientMessageTypes
    {
        public const string Ping = "ping";
        public const string RoomSubscribe = "room.subscribe";
        public const string RoomUnsubscribe = "room.unsubscribe";
    }

    /// <summary>서버 error 메시지의 code 값.</summary>
    public static class RealtimeErrorCodes
    {
        public const string BadMessage = "BAD_MESSAGE";
        public const string UnknownType = "UNKNOWN_TYPE";
        public const string NotInRoom = "NOT_IN_ROOM";
        public const string ServerError = "SERVER_ERROR";
    }
}
