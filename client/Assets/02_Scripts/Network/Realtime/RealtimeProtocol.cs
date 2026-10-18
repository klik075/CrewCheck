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

        /// <summary>서버가 연결을 닫을 때 쓰는 코드: 같은 계정으로 다른 곳에서 새로 로그인했다. 다시 연결하지 않고 로그아웃한다.</summary>
        public const int CloseDuplicateLogin = 4409;

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

        /// <summary>
        /// 토큰을 주소에 담을 때 쓰는 쿼리 이름. 서버 RealtimeProtocol.TOKEN_QUERY_PARAM과 같다.
        /// 브라우저(WebGL)의 WebSocket은 Authorization 헤더를 넣을 수 없어서 이 방법을 쓴다.
        /// </summary>
        public const string TokenQueryParam = "token";

        /// <summary>
        /// WebSocket 주소 뒤에 ?token=토큰 을 붙인다. (WebGL 전용. 데스크톱은 Authorization 헤더를 쓴다)
        ///   ws://host/ws/v1        → ws://host/ws/v1?token=eyJ...
        ///   ws://host/ws/v1?a=1    → ws://host/ws/v1?a=1&amp;token=eyJ...
        /// 토큰은 URL 인코딩한다. JWT는 인코딩해도 글자가 바뀌지 않는다(A-Z a-z 0-9 - _ . 만 씀).
        /// 서버는 쿼리 값을 디코딩하지 않고 읽으므로, JWT가 아닌 토큰을 넣을 일이 생기면 서버도 함께 확인한다.
        /// 토큰이 비어 있으면 주소를 그대로 돌려준다. (연결해도 서버가 401로 거절한다)
        /// 이미 token 쿼리가 있는 주소는 넣지 않는다고 가정한다. (BuildUrl이 만든 주소에는 쿼리가 없다)
        /// </summary>
        public static string WithTokenQuery(string url, string accessToken)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                throw new ArgumentException("url이 비어 있습니다.", nameof(url));
            }
            if (string.IsNullOrWhiteSpace(accessToken))
            {
                return url;
            }

            // #조각(fragment)이 있으면 쿼리는 그 앞에 붙어야 한다.
            string fragment = string.Empty;
            int hash = url.IndexOf('#');
            if (hash >= 0)
            {
                fragment = url.Substring(hash);
                url = url.Substring(0, hash);
            }

            string separator;
            if (url.IndexOf('?') < 0)
            {
                separator = "?";
            }
            else if (url.EndsWith("?", StringComparison.Ordinal) || url.EndsWith("&", StringComparison.Ordinal))
            {
                separator = string.Empty;
            }
            else
            {
                separator = "&";
            }

            return url + separator + TokenQueryParam + "=" + Uri.EscapeDataString(accessToken.Trim()) + fragment;
        }

        /// <summary>
        /// 로그에 남기기 전에 주소 속 토큰 값을 ***로 가린다. 토큰이 없으면 그대로.
        ///   ws://host/ws/v1?token=eyJ...  → ws://host/ws/v1?token=***
        /// 토큰은 로그인 출입증이라 콘솔(브라우저 개발자 도구 포함)에 그대로 찍히면 안 된다.
        /// </summary>
        public static string RedactToken(string url)
        {
            if (string.IsNullOrEmpty(url))
            {
                return url;
            }

            string key = TokenQueryParam + "=";
            int searchFrom = 0;
            while (true)
            {
                int at = url.IndexOf(key, searchFrom, StringComparison.Ordinal);
                if (at < 0)
                {
                    return url;
                }
                // 쿼리 이름의 시작인지 확인한다. (예: mytoken= 는 건드리지 않는다)
                bool isParamStart = at > 0 && (url[at - 1] == '?' || url[at - 1] == '&');
                int valueStart = at + key.Length;
                if (!isParamStart)
                {
                    searchFrom = valueStart;
                    continue;
                }
                int valueEnd = url.IndexOfAny(new[] { '&', '#' }, valueStart);
                if (valueEnd < 0)
                {
                    valueEnd = url.Length;
                }
                url = url.Substring(0, valueStart) + "***" + url.Substring(valueEnd);
                searchFrom = valueStart + 3;
            }
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
        public const string RoomCreated = "room.created";
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
