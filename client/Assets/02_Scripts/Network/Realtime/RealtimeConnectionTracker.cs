using System;

namespace WhoisntCitizen.Network.Realtime
{
    /// <summary>WebSocket 연결 상태.</summary>
    public enum RealtimeState
    {
        /// <summary>연결이 없다. 화면은 빠른 폴링(1~2초)으로 진행한다. 백오프 뒤 다시 연결을 시도한다.</summary>
        Disconnected,

        /// <summary>소켓을 여는 중이거나, 열었지만 서버의 connected를 아직 못 받았다.</summary>
        Connecting,

        /// <summary>서버가 connected로 인증을 확인했다. 아직 방 신호는 오지 않는다(방 구독 전, 또는 방 밖).</summary>
        Connected,

        /// <summary>방 구독이 확인됐다(subscribed 수신). 이 방의 room/game/chat.changed가 온다.</summary>
        Subscribed
    }

    /// <summary>상태가 바뀐 결과 RealtimeClient가 서버로 보내야 할 메시지.</summary>
    public enum RealtimeActionKind
    {
        None,
        Subscribe,
        Unsubscribe
    }

    /// <summary>보낼 메시지 하나. Kind가 None이면 보낼 것이 없다.</summary>
    public readonly struct RealtimeAction : IEquatable<RealtimeAction>
    {
        public static readonly RealtimeAction None = new RealtimeAction(RealtimeActionKind.None, 0);

        public RealtimeActionKind Kind { get; }
        public long RoomId { get; }

        private RealtimeAction(RealtimeActionKind kind, long roomId)
        {
            Kind = kind;
            RoomId = roomId;
        }

        public static RealtimeAction Subscribe(long roomId) => new RealtimeAction(RealtimeActionKind.Subscribe, roomId);
        public static RealtimeAction Unsubscribe(long roomId) => new RealtimeAction(RealtimeActionKind.Unsubscribe, roomId);

        public bool Equals(RealtimeAction other) => Kind == other.Kind && RoomId == other.RoomId;
        public override bool Equals(object obj) => obj is RealtimeAction other && Equals(other);
        public override int GetHashCode() => ((int)Kind * 397) ^ RoomId.GetHashCode();
        public override string ToString() => Kind == RealtimeActionKind.None ? "None" : Kind + "(" + RoomId + ")";
    }

    /// <summary>
    /// 연결 상태와 방 구독을 기억하고, 사건이 생길 때마다 "다음 상태"와 "서버로 보낼 것"을 정한다.
    /// 소켓을 직접 다루지 않는다. RealtimeClient가 사건을 알려 주고, 돌려받은 RealtimeAction을 보낸다.
    ///
    ///   Disconnected ─OnConnecting→ Connecting ─OnConnected→ Connected ─OnSubscribed→ Subscribed
    ///        ↑                                                                          │
    ///        └────────────────────────── OnDisconnected (어느 상태에서든) ─────────────────┘
    ///
    /// 방 두 가지를 구분한다.
    ///  - DesiredRoomId: 지금 화면이 보고 있는 방(들어가 있는 방). 연결이 끊겨도 기억한다 → 다시 연결되면 자동으로 구독.
    ///  - SubscribedRoomId: 서버가 subscribed로 확인해 준 방. 연결이 끊기면 0.
    ///
    /// 메인 스레드에서만 부른다. (RealtimeClient가 수신 스레드의 메시지를 메인 스레드로 옮긴 뒤 부른다)
    /// </summary>
    public sealed class RealtimeConnectionTracker
    {
        public RealtimeState State { get; private set; } = RealtimeState.Disconnected;

        /// <summary>connected에서 받은 내 userId. 연결 전이면 0.</summary>
        public long UserId { get; private set; }

        /// <summary>화면이 보고 있는 방. 방 밖이면 0.</summary>
        public long DesiredRoomId { get; private set; }

        /// <summary>서버가 구독을 확인한 방. Subscribed 상태에서만 0이 아니다.</summary>
        public long SubscribedRoomId { get; private set; }

        /// <summary>State가 바뀔 때마다 (이전, 이후). 화면이 폴링 주기를 다시 정할 때 쓴다.</summary>
        public event Action<RealtimeState, RealtimeState> StateChanged;

        // ------------------------------------------------------------ 연결 사건

        /// <summary>소켓을 열기 시작한다. Disconnected일 때만 의미가 있다. 상태가 바뀌었으면 true.</summary>
        public bool OnConnecting()
        {
            if (State != RealtimeState.Disconnected)
            {
                return false;
            }
            SetState(RealtimeState.Connecting);
            return true;
        }

        /// <summary>
        /// 서버의 connected를 받았다. 보고 있는 방이 있으면 Subscribe를 돌려준다(재연결 때 자동 재구독).
        /// Connecting이 아닐 때(중복, 끊긴 뒤 늦게 도착) 온 것은 무시한다.
        /// </summary>
        public RealtimeAction OnConnected(long userId)
        {
            if (State != RealtimeState.Connecting)
            {
                return RealtimeAction.None;
            }
            UserId = userId;
            SetState(RealtimeState.Connected);
            return DesiredRoomId > 0 ? RealtimeAction.Subscribe(DesiredRoomId) : RealtimeAction.None;
        }

        /// <summary>
        /// 서버의 subscribed를 받았다. 지금 보고 있는 방에 대한 답이면 Subscribed가 되고 true.
        /// true면 호출한 쪽은 방·게임·채팅을 GET으로 한 번 가져와야 한다(구독 전·끊긴 동안의 변경을 메우기 위해).
        /// 이미 떠난 방에 대한 늦은 답이나 연결 전 답은 무시하고 false.
        /// </summary>
        public bool OnSubscribed(long roomId)
        {
            bool connected = State == RealtimeState.Connected || State == RealtimeState.Subscribed;
            if (!connected || roomId <= 0 || roomId != DesiredRoomId)
            {
                return false;
            }
            bool already = State == RealtimeState.Subscribed && SubscribedRoomId == roomId;
            SubscribedRoomId = roomId;
            SetState(RealtimeState.Subscribed);
            return !already;
        }

        /// <summary>
        /// 서버가 구독을 거절했다(error NOT_IN_ROOM). 방 신호가 오지 않으니 Connected로 돌아가 빠른 폴링을 하게 한다.
        /// 보고 있는 방은 지우지 않는다. 방 화면의 GET이 실제로 방에서 나갔는지 판단해 LeaveRoom을 부른다.
        /// </summary>
        public void OnSubscribeRejected()
        {
            if (State == RealtimeState.Subscribed || State == RealtimeState.Connected)
            {
                SubscribedRoomId = 0;
                SetState(RealtimeState.Connected);
            }
        }

        /// <summary>
        /// 연결이 끊겼다(서버가 닫음, 네트워크 오류, 연결 실패 모두). 구독도 같이 풀린다.
        /// 보고 있는 방은 기억해 두었다가 다시 연결되면 구독한다.
        /// </summary>
        public void OnDisconnected()
        {
            UserId = 0;
            SubscribedRoomId = 0;
            SetState(RealtimeState.Disconnected);
        }

        // ------------------------------------------------------------ 화면 사건

        /// <summary>
        /// 방에 들어갔다(방 화면·게임 화면 진입). 연결돼 있으면 Subscribe를 돌려준다.
        /// 끊겨 있으면 기억만 하고, OnConnected 때 구독한다.
        /// 다른 방을 구독 중이었다면 서버가 이전 구독을 알아서 바꾸므로 Unsubscribe는 따로 보내지 않는다.
        /// </summary>
        public RealtimeAction EnterRoom(long roomId)
        {
            if (roomId <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(roomId), roomId, "roomId는 양수여야 합니다.");
            }

            bool sameRoom = roomId == DesiredRoomId;
            DesiredRoomId = roomId;

            switch (State)
            {
                case RealtimeState.Connected:
                    // 같은 방에 다시 들어와도(씬 전환 등) 구독 확인이 아직 없으면 다시 보낸다. 서버는 중복 구독을 문제 삼지 않는다.
                    return RealtimeAction.Subscribe(roomId);

                case RealtimeState.Subscribed:
                    if (sameRoom && SubscribedRoomId == roomId)
                    {
                        return RealtimeAction.None; // 이미 이 방을 구독 중 (Room → GameScene 전환)
                    }
                    SubscribedRoomId = 0;
                    SetState(RealtimeState.Connected); // 새 방의 subscribed가 올 때까지는 신호를 믿지 않는다
                    return RealtimeAction.Subscribe(roomId);

                default:
                    return RealtimeAction.None;
            }
        }

        /// <summary>
        /// 방에서 나갔다(퇴장·강퇴·방 삭제·로비 복귀). 구독 중이면 Unsubscribe를 돌려준다.
        /// 연결 자체는 유지한다(로비에서도 다음 입장을 위해).
        /// </summary>
        public RealtimeAction LeaveRoom()
        {
            long previous = DesiredRoomId;
            DesiredRoomId = 0;
            if (previous <= 0)
            {
                return RealtimeAction.None;
            }

            if (State == RealtimeState.Subscribed)
            {
                SubscribedRoomId = 0;
                SetState(RealtimeState.Connected);
                return RealtimeAction.Unsubscribe(previous);
            }
            if (State == RealtimeState.Connected)
            {
                // 구독 요청을 보냈지만 답이 아직 없을 수 있다. 서버는 그 방이 현재 구독일 때만 풀므로 보내도 안전하다.
                return RealtimeAction.Unsubscribe(previous);
            }
            return RealtimeAction.None;
        }

        // ------------------------------------------------------------ 조회

        /// <summary>
        /// 이 방의 변경 신호를 지금 받고 있나. true일 때만 화면이 폴링을 안전망(15초)으로 늦춘다.
        /// 연결은 됐어도 구독 전이거나 다른 방을 구독 중이면 false → 빠른 폴링.
        /// </summary>
        public bool ReceivesSignalsFor(long roomId)
        {
            return roomId > 0 && State == RealtimeState.Subscribed && SubscribedRoomId == roomId;
        }

        private void SetState(RealtimeState next)
        {
            if (State == next)
            {
                return;
            }
            RealtimeState previous = State;
            State = next;
            StateChanged?.Invoke(previous, next);
        }
    }
}
