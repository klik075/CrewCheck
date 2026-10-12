using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace WhoisntCitizen.Network.Realtime.Tests
{
    public class ReconnectBackoffTests
    {
        private static ReconnectBackoff NoJitter()
        {
            return new ReconnectBackoff(jitterRatio: 0);
        }

        private static List<double> Take(ReconnectBackoff backoff, int count)
        {
            var delays = new List<double>();
            for (int i = 0; i < count; i++)
            {
                delays.Add(backoff.NextDelaySeconds());
            }
            return delays;
        }

        // ------------------------------------------------------------ 기본 규칙

        [Test]
        public void 대기시간은_1_2_4_8_16초로_두_배씩_늘고_30초에서_멈춘다()
        {
            CollectionAssert.AreEqual(
                new[] { 1.0, 2.0, 4.0, 8.0, 16.0, 30.0, 30.0, 30.0 },
                Take(NoJitter(), 8));
        }

        [Test]
        public void 실패할_때마다_Attempt가_1씩_는다()
        {
            var backoff = NoJitter();
            Assert.AreEqual(0, backoff.Attempt);
            backoff.NextDelaySeconds();
            backoff.NextDelaySeconds();
            Assert.AreEqual(2, backoff.Attempt);
        }

        [Test]
        public void Reset하면_다시_1초부터_시작한다()
        {
            var backoff = NoJitter();
            Take(backoff, 5); // 1, 2, 4, 8, 16
            backoff.Reset();

            Assert.AreEqual(0, backoff.Attempt);
            CollectionAssert.AreEqual(new[] { 1.0, 2.0 }, Take(backoff, 2));
        }

        [Test]
        public void 아주_많이_실패해도_계산이_넘치지_않고_상한을_지킨다()
        {
            var backoff = NoJitter();
            Assert.AreEqual(30.0, backoff.BaseDelaySeconds(1000));
            Assert.AreEqual(30.0, backoff.BaseDelaySeconds(int.MaxValue));

            var delays = Take(backoff, 200);
            Assert.AreEqual(30.0, delays[delays.Count - 1]);
        }

        [Test]
        public void 첫_대기시간과_상한을_바꿀_수_있다()
        {
            var backoff = new ReconnectBackoff(initialSeconds: 0.5, maxSeconds: 3, jitterRatio: 0);
            CollectionAssert.AreEqual(new[] { 0.5, 1.0, 2.0, 3.0, 3.0 }, Take(backoff, 5));
        }

        [Test]
        public void 첫_대기시간과_상한이_같으면_항상_그_값이다()
        {
            var backoff = new ReconnectBackoff(initialSeconds: 5, maxSeconds: 5, jitterRatio: 0);
            CollectionAssert.AreEqual(new[] { 5.0, 5.0, 5.0 }, Take(backoff, 3));
        }

        // ------------------------------------------------------------ 지터

        [Test]
        public void 지터_가운데값이면_기본값_그대로다()
        {
            var backoff = new ReconnectBackoff(jitterRatio: 0.2, random: () => 0.5);
            CollectionAssert.AreEqual(new[] { 1.0, 2.0, 4.0 }, Take(backoff, 3));
        }

        [Test]
        public void 지터는_기본값의_위아래_20퍼센트_안에서_흔든다()
        {
            var low = new ReconnectBackoff(jitterRatio: 0.2, random: () => 0.0);
            Assert.AreEqual(0.8, low.NextDelaySeconds(), 1e-9);  // 1초 × 0.8
            Assert.AreEqual(1.6, low.NextDelaySeconds(), 1e-9);  // 2초 × 0.8

            var high = new ReconnectBackoff(jitterRatio: 0.2, random: () => 0.999999);
            Assert.AreEqual(1.2, high.NextDelaySeconds(), 1e-4); // 1초 × 1.2
            Assert.AreEqual(2.4, high.NextDelaySeconds(), 1e-4); // 2초 × 1.2
        }

        [Test]
        public void 지터를_섞어도_상한_30초는_넘지_않는다()
        {
            var backoff = new ReconnectBackoff(jitterRatio: 0.2, random: () => 0.999999);
            var delays = Take(backoff, 10);
            foreach (double d in delays)
            {
                Assert.LessOrEqual(d, 30.0);
            }
            Assert.AreEqual(30.0, delays[9]);
        }

        [Test]
        public void 기본_무작위로도_항상_범위_안이다()
        {
            var backoff = new ReconnectBackoff();
            for (int i = 0; i < 500; i++)
            {
                double expectedBase = backoff.BaseDelaySeconds(backoff.Attempt);
                double d = backoff.NextDelaySeconds();
                Assert.GreaterOrEqual(d, expectedBase * 0.8 - 1e-9);
                Assert.LessOrEqual(d, Math.Min(30.0, expectedBase * 1.2));
                if (i % 7 == 6) backoff.Reset();
            }
        }

        [TestCase(-0.1)]
        [TestCase(1.0)]
        [TestCase(double.NaN)]
        public void random이_범위를_벗어난_값을_주면_흔들지_않는다(double bad)
        {
            var backoff = new ReconnectBackoff(jitterRatio: 0.2, random: () => bad);
            CollectionAssert.AreEqual(new[] { 1.0, 2.0 }, Take(backoff, 2));
        }

        // ------------------------------------------------------------ 잘못된 설정

        [TestCase(0.0, 30.0, 0.2)]
        [TestCase(-1.0, 30.0, 0.2)]
        [TestCase(double.NaN, 30.0, 0.2)]
        [TestCase(5.0, 4.0, 0.2)]
        [TestCase(1.0, double.NaN, 0.2)]
        [TestCase(1.0, 30.0, -0.1)]
        [TestCase(1.0, 30.0, 1.0)]
        [TestCase(1.0, 30.0, double.NaN)]
        public void 잘못된_설정은_만들_때_막는다(double initial, double max, double jitter)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new ReconnectBackoff(initial, max, jitter));
        }

        [Test]
        public void 음수_시도횟수는_막는다()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => NoJitter().BaseDelaySeconds(-1));
        }
    }
}
