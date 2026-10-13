namespace WhoisntCitizen.Network.Realtime
{
    /// <summary>한 프레임 동안 모인 변경 신호를 화면에 넘길 모양으로 정리한 것.</summary>
    public readonly struct RealtimeSignals
    {
        public static readonly RealtimeSignals Empty = default;

        /// <summary>신호가 온 방. 아무 신호도 없으면 0.</summary>
        public long RoomId { get; }

        /// <summary>room.changed가 한 번 이상 왔다 → GET /rooms/{roomId}</summary>
        public bool RoomChanged { get; }

        /// <summary>game.changed 중 가장 최근 것. 없으면 null → 있으면 GET /games/{gameId}</summary>
        public GameChangedMessage Game { get; }

        /// <summary>chat.changed 중 lastMessageId가 가장 큰 것. 없으면 null → 있으면 GET messages?afterId=…</summary>
        public ChatChangedMessage Chat { get; }

        public bool IsEmpty => !RoomChanged && Game == null && Chat == null;

        public RealtimeSignals(long roomId, bool roomChanged, GameChangedMessage game, ChatChangedMessage chat)
        {
            RoomId = roomId;
            RoomChanged = roomChanged;
            Game = game;
            Chat = chat;
        }
    }

    /// <summary>
    /// 서버의 *.changed 신호 중 화면이 반응할 것만 골라, 한 프레임에 종류별로 한 번씩만 넘긴다.
    ///
    /// 거르는 것
    ///  - 구독이 확인되지 않은 상태의 신호 (구독 전, 다른 방으로 옮기는 중, 끊긴 뒤)
    ///  - 지금 구독한 방이 아닌 방의 room/chat 신호 (방을 옮기기 직전에 출발한 늦은 신호)
    ///  - 이미 신호로 알린 채팅 id 이하의 chat.changed (중복·순서 뒤바뀜)
    ///
    /// 합치는 것 (같은 프레임 안)
    ///  - room.changed 여러 번 → 한 번  (입장·준비가 몰려도 GET은 한 번)
    ///  - game.changed 여러 번 → phaseVersion이 가장 큰 것 하나
    ///  - chat.changed 여러 번 → lastMessageId가 가장 큰 것 하나
    ///
    /// game.changed는 방 번호가 없다. 서버는 구독한 방의 게임 신호만 보내므로 "구독 중"이면 받는다.
    /// phaseVersion은 투표처럼 페이즈가 그대로여도 바뀌는 일이 있어 거르는 데 쓰지 않는다.
    ///
    /// 메인 스레드에서만 쓴다. RealtimeClient가 Offer로 넣고, 프레임 끝에 TakePending으로 꺼낸다.
    /// </summary>
    public sealed class RealtimeSignalFilter
    {
        private long pendingRoomId;
        private bool pendingRoomChanged;
        private GameChangedMessage pendingGame;
        private ChatChangedMessage pendingChat;

        // 이 방에서 지금까지 화면에 넘긴(또는 넘길) 가장 큰 채팅 id
        private long chatRoomId;
        private long maxChatId;

        /// <summary>
        /// 신호 하나를 넣는다. 받아들였으면 true.
        /// subscribedRoomId는 RealtimeConnectionTracker.SubscribedRoomId(구독 확인 전이면 0).
        /// *.changed가 아닌 메시지는 false.
        /// </summary>
        public bool Offer(RealtimeServerMessage message, long subscribedRoomId)
        {
            if (subscribedRoomId <= 0 || message == null)
            {
                return false;
            }

            switch (message)
            {
                case RoomChangedMessage room:
                    if (room.RoomId != subscribedRoomId) return false;
                    PrepareFor(subscribedRoomId);
                    pendingRoomChanged = true;
                    return true;

                case GameChangedMessage game:
                    PrepareFor(subscribedRoomId);
                    if (pendingGame == null || game.PhaseVersion >= pendingGame.PhaseVersion)
                    {
                        pendingGame = game;
                    }
                    return true;

                case ChatChangedMessage chat:
                    if (chat.RoomId != subscribedRoomId) return false;
                    if (chatRoomId != subscribedRoomId)
                    {
                        chatRoomId = subscribedRoomId;
                        maxChatId = 0;
                    }
                    if (chat.LastMessageId <= maxChatId) return false;
                    maxChatId = chat.LastMessageId;
                    PrepareFor(subscribedRoomId);
                    pendingChat = chat;
                    return true;

                default:
                    return false;
            }
        }

        /// <summary>모인 신호를 꺼내고 비운다. 없으면 Empty.</summary>
        public RealtimeSignals TakePending()
        {
            if (!pendingRoomChanged && pendingGame == null && pendingChat == null)
            {
                return RealtimeSignals.Empty;
            }
            var signals = new RealtimeSignals(pendingRoomId, pendingRoomChanged, pendingGame, pendingChat);
            ClearPending();
            return signals;
        }

        /// <summary>
        /// 구독이 (다시) 확인됐다. 화면이 어차피 전부 GET하므로 모아 둔 신호는 버리고,
        /// 채팅 기준 id도 처음부터 센다(끊긴 동안 기준이 틀어졌을 수 있다).
        /// </summary>
        public void OnResubscribed()
        {
            ClearPending();
            chatRoomId = 0;
            maxChatId = 0;
        }

        /// <summary>연결이 끊겼거나 방을 나갔다. 아직 넘기지 않은 신호는 버린다.</summary>
        public void Clear()
        {
            OnResubscribed();
        }

        private void PrepareFor(long roomId)
        {
            if (pendingRoomId != roomId)
            {
                ClearPending(); // 다른 방의 신호와 섞이지 않게
                pendingRoomId = roomId;
            }
        }

        private void ClearPending()
        {
            pendingRoomId = 0;
            pendingRoomChanged = false;
            pendingGame = null;
            pendingChat = null;
        }
    }
}
