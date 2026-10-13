using NUnit.Framework;

namespace WhoisntCitizen.Network.Realtime.Tests
{
    public class RealtimePollingTests
    {
        [TestCase(1.0, 1.0)]
        [TestCase(2.0, 2.0)]
        public void 신호가_없으면_화면_원래_주기로_폴링한다(double fast, double expected)
        {
            Assert.AreEqual(expected, RealtimePolling.IntervalSeconds(false, fast));
        }

        [TestCase(1.0)]
        [TestCase(2.0)]
        public void 신호를_받는_중이면_15초_안전망으로_늦춘다(double fast)
        {
            Assert.AreEqual(15.0, RealtimePolling.IntervalSeconds(true, fast));
        }

        [Test]
        public void 원래_주기가_15초보다_길면_그대로_둔다()
        {
            Assert.AreEqual(20.0, RealtimePolling.IntervalSeconds(true, 20.0));
        }

        [TestCase(0.0)]
        [TestCase(-1.0)]
        [TestCase(0.05)]
        [TestCase(double.NaN)]
        public void 너무_짧거나_잘못된_주기는_하한으로_올린다(double fast)
        {
            Assert.AreEqual(RealtimePolling.MinSeconds, RealtimePolling.IntervalSeconds(false, fast));
        }

        [Test]
        public void 상태_추적기와_함께_쓰면_구독_여부에_따라_주기가_바뀐다()
        {
            var tracker = new RealtimeConnectionTracker();
            double Interval() => RealtimePolling.IntervalSeconds(tracker.ReceivesSignalsFor(3), 1.0);

            Assert.AreEqual(1.0, Interval());   // 끊김
            tracker.OnConnecting();
            tracker.OnConnected(7);
            tracker.EnterRoom(3);
            Assert.AreEqual(1.0, Interval());   // 구독 답 대기
            tracker.OnSubscribed(3);
            Assert.AreEqual(15.0, Interval());  // 신호 받는 중
            tracker.OnDisconnected();
            Assert.AreEqual(1.0, Interval());   // 끊기면 바로 빠른 폴링
        }
    }
}
