using System.Collections.Generic;

namespace WhoisntCitizen.Network.Realtime
{
    /// <summary>
    /// transport가 만든 사건을 쌓아 두는 수신함. 넣는 쪽(Post)과 꺼내는 쪽(TryDequeue)이 다른 스레드여도 안전하다.
    ///
    ///   ClientWebSocketTransport  : 받기 스레드(백그라운드)가 Post → RealtimeClient.Update(메인 스레드)가 TryDequeue
    ///   BrowserWebSocketTransport : 브라우저 콜백(메인 스레드)이 Post → 같은 메인 스레드가 TryDequeue
    ///
    /// IRealtimeTransport 약속 중 "사건 순서"를 여기서 지킨다. transport 구현은 사건을 그냥 Post하면 된다.
    ///  - Opened는 맨 앞에 한 번만. 두 번째 Opened, Message 뒤의 Opened는 버린다.
    ///  - Message는 Opened 뒤에만. Opened 전의 Message는 버린다. (연결도 안 됐는데 받은 메시지는 있을 수 없다)
    ///  - Closed는 딱 한 번, 맨 마지막. Closed 뒤에 들어오는 것은 무엇이든 버린다. (두 번째 Closed 포함)
    ///    연결에 실패하면 Opened 없이 Closed만 들어와도 된다.
    ///  - 꺼내는 순서는 넣은 순서와 같다. (먼저 넣은 것이 먼저 나온다, FIFO)
    /// 버린 사건은 Post가 false를 돌려줘서 알 수 있다. (예외는 던지지 않는다)
    ///
    /// 왜 ConcurrentQueue가 아니라 lock + Queue인가
    ///  "Closed가 이미 들어왔는지 확인"과 "큐에 넣기"가 한 덩어리로 실행돼야 한다.
    ///  ConcurrentQueue는 넣기 하나하나만 안전해서, 스레드 A가 확인(아직 안 닫힘)과 넣기 사이에 멈춘 동안
    ///  스레드 B가 Closed를 넣으면 A의 Message가 Closed 뒤에 들어갈 수 있다.
    ///  lock으로 묶으면 그런 끼어들기가 없다. 사건은 많아야 초당 몇 개라 잠금 비용은 문제가 되지 않는다.
    /// </summary>
    public sealed class RealtimeTransportInbox
    {
        private readonly object gate = new object();
        private readonly Queue<RealtimeTransportEvent> queue = new Queue<RealtimeTransportEvent>();
        private bool opened;
        private bool hasMessage;
        private bool closed;

        /// <summary>Closed를 받았으면 true. (아직 꺼내지 않았어도 true)</summary>
        public bool IsClosed
        {
            get { lock (gate) { return closed; } }
        }

        /// <summary>아직 꺼내지 않은 사건 수 (테스트·점검용).</summary>
        public int Count
        {
            get { lock (gate) { return queue.Count; } }
        }

        /// <summary>사건 하나를 넣는다. 순서 규칙에 맞지 않아 버렸으면 false.</summary>
        public bool Post(RealtimeTransportEvent transportEvent)
        {
            if (transportEvent == null)
            {
                return false;
            }
            lock (gate)
            {
                if (closed)
                {
                    return false; // Closed 뒤에는 아무것도 받지 않는다
                }
                switch (transportEvent.Kind)
                {
                    case RealtimeTransportEventKind.Opened:
                        if (opened || hasMessage)
                        {
                            return false;
                        }
                        opened = true;
                        break;
                    case RealtimeTransportEventKind.Message:
                        if (!opened)
                        {
                            return false;
                        }
                        hasMessage = true;
                        break;
                    case RealtimeTransportEventKind.Closed:
                        closed = true;
                        break;
                }
                queue.Enqueue(transportEvent);
                return true;
            }
        }

        /// <summary>연결 성공을 넣는다.</summary>
        public bool PostOpened()
        {
            return Post(RealtimeTransportEvent.Opened());
        }

        /// <summary>받은 텍스트 메시지를 넣는다.</summary>
        public bool PostMessage(string text)
        {
            return Post(RealtimeTransportEvent.Message(text));
        }

        /// <summary>연결 종료(또는 연결 실패)를 넣는다. 두 번째부터는 버린다.</summary>
        public bool PostClosed(int? closeCode, string reason, bool unauthorized = false)
        {
            return Post(RealtimeTransportEvent.Closed(closeCode, reason, unauthorized));
        }

        /// <summary>가장 먼저 넣은 사건 하나를 꺼낸다. 없으면 false, transportEvent는 null.</summary>
        public bool TryDequeue(out RealtimeTransportEvent transportEvent)
        {
            lock (gate)
            {
                if (queue.Count > 0)
                {
                    transportEvent = queue.Dequeue();
                    return true;
                }
            }
            transportEvent = null;
            return false;
        }
    }
}
