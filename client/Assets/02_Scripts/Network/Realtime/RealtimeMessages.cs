namespace WhoisntCitizen.Network.Realtime
{
    /// <summary>
    /// 서버 → 클라이언트 메시지. RealtimeMessageCodec.TryDecode가 type에 맞는 하위 클래스를 만들어 준다.
    /// 받는 쪽은 switch 패턴으로 나눠 쓴다.
    ///
    ///   switch (message)
    ///   {
    ///       case GameChangedMessage g: ...; break;
    ///       case ChatChangedMessage c: ...; break;
    ///   }
    ///
    /// *.changed는 "바뀌었으니 GET으로 다시 가져가라"는 신호일 뿐 내용이 아니다.
    /// </summary>
    public abstract class RealtimeServerMessage
    {
        /// <summary>JSON의 type 값 그대로.</summary>
        public abstract string Type { get; }
    }

    /// <summary>연결 직후 한 번. 누구로 인증됐는지와 ping 주기를 알려 준다. 받은 뒤 room.subscribe를 보낸다.</summary>
    public sealed class ConnectedMessage : RealtimeServerMessage
    {
        public override string Type => ServerMessageTypes.Connected;
        public long UserId { get; }
        public long HeartbeatSeconds { get; }
        public long IdleTimeoutSeconds { get; }

        public ConnectedMessage(long userId, long heartbeatSeconds, long idleTimeoutSeconds)
        {
            UserId = userId;
            HeartbeatSeconds = heartbeatSeconds;
            IdleTimeoutSeconds = idleTimeoutSeconds;
        }
    }

    /// <summary>ping에 대한 답. ServerTimeMs는 서버 시각(epoch ms).</summary>
    public sealed class PongMessage : RealtimeServerMessage
    {
        public override string Type => ServerMessageTypes.Pong;
        public long ServerTimeMs { get; }

        public PongMessage(long serverTimeMs)
        {
            ServerTimeMs = serverTimeMs;
        }
    }

    /// <summary>room.subscribe가 받아들여졌다. 받으면 방·게임·채팅을 GET으로 한 번 가져온다(구독 전 변경을 놓치지 않게).</summary>
    public sealed class SubscribedMessage : RealtimeServerMessage
    {
        public override string Type => ServerMessageTypes.Subscribed;
        public long RoomId { get; }

        public SubscribedMessage(long roomId)
        {
            RoomId = roomId;
        }
    }

    /// <summary>게임 상태가 바뀌었다 → GET /api/v1/games/{gameId}. PhaseVersion은 참고용(투표처럼 페이즈가 그대로여도 온다).</summary>
    public sealed class GameChangedMessage : RealtimeServerMessage
    {
        public override string Type => ServerMessageTypes.GameChanged;
        public string GameId { get; }
        public long PhaseVersion { get; }

        public GameChangedMessage(string gameId, long phaseVersion)
        {
            GameId = gameId;
            PhaseVersion = phaseVersion;
        }
    }

    /// <summary>방 정보가 바뀌었다 → GET /api/v1/rooms/{roomId}.</summary>
    public sealed class RoomChangedMessage : RealtimeServerMessage
    {
        public override string Type => ServerMessageTypes.RoomChanged;
        public long RoomId { get; }

        public RoomChangedMessage(long roomId)
        {
            RoomId = roomId;
        }
    }

    /// <summary>
    /// 로비에 방이 새로 생겼다. 방을 구독하지 않은 연결(로비)에만 오고, 만든 사람은 받지 않는다.
    /// 로비는 목록을 바꾸지 않고 새로고침 버튼의 RedDot만 켠다.
    /// </summary>
    public sealed class RoomCreatedMessage : RealtimeServerMessage
    {
        public override string Type => ServerMessageTypes.RoomCreated;
        public long RoomId { get; }

        public RoomCreatedMessage(long roomId)
        {
            RoomId = roomId;
        }
    }

    /// <summary>새 채팅이 생겼다 → GET messages?afterId=(마지막 id). 이미 LastMessageId까지 받았다면 무시해도 된다.</summary>
    public sealed class ChatChangedMessage : RealtimeServerMessage
    {
        public override string Type => ServerMessageTypes.ChatChanged;
        public long RoomId { get; }
        public long LastMessageId { get; }

        public ChatChangedMessage(long roomId, long lastMessageId)
        {
            RoomId = roomId;
            LastMessageId = lastMessageId;
        }
    }

    /// <summary>내가 보낸 메시지를 서버가 처리하지 못했다. 연결은 유지된다. Code는 RealtimeErrorCodes 값.</summary>
    public sealed class RealtimeErrorMessage : RealtimeServerMessage
    {
        public override string Type => ServerMessageTypes.Error;
        public string Code { get; }
        public string Message { get; }

        public RealtimeErrorMessage(string code, string message)
        {
            Code = code;
            Message = message;
        }
    }

    /// <summary>
    /// 이 클라이언트가 모르는 type. 서버가 나중에 새 메시지를 추가해도 연결을 끊지 않고 무시할 수 있게 한다.
    /// </summary>
    public sealed class UnknownServerMessage : RealtimeServerMessage
    {
        private readonly string type;
        public override string Type => type;

        public UnknownServerMessage(string type)
        {
            this.type = type;
        }
    }
}
