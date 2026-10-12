using System;

namespace WhoisntCitizen.Network.Realtime
{
    /// <summary>
    /// 화면이 GET 폴링을 얼마나 자주 할지 정한다.
    ///
    ///  - 방 신호를 받는 중(Subscribed, 같은 방): 15초. 신호가 오면 바로 GET하므로 폴링은 혹시 놓친 신호를 메우는 안전망이다.
    ///  - 그 밖(끊김·연결 중·구독 전·다른 방): 화면 원래 주기(게임·방 1초, 채팅 2초). 신호 없이도 지금처럼 진행된다.
    ///
    /// 빠른 주기는 화면마다 달라서 호출하는 쪽이 넘긴다.
    /// </summary>
    public static class RealtimePolling
    {
        /// <summary>신호를 받는 동안의 안전망 폴링 주기(초).</summary>
        public const double SafetyNetSeconds = 15.0;

        /// <summary>너무 잦은 폴링을 막는 하한(초). 기존 GameSession·RoomUIController와 같은 값.</summary>
        public const double MinSeconds = 0.2;

        /// <param name="receivesSignals">RealtimeConnectionTracker.ReceivesSignalsFor(이 화면의 방)</param>
        /// <param name="fastSeconds">신호가 없을 때 쓸 이 화면의 원래 주기</param>
        public static double IntervalSeconds(bool receivesSignals, double fastSeconds)
        {
            if (double.IsNaN(fastSeconds) || fastSeconds < MinSeconds)
            {
                fastSeconds = MinSeconds;
            }
            // 원래 주기가 15초보다 길면 신호가 있다고 더 자주 할 이유는 없다.
            return receivesSignals ? Math.Max(fastSeconds, SafetyNetSeconds) : fastSeconds;
        }
    }
}
