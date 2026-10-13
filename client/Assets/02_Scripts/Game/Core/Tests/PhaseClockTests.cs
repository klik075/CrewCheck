using NUnit.Framework;

namespace WhoisntCitizen.Game.Tests
{
    public class PhaseClockTests
    {
        private static GameStateDto State(long phaseVersion, string phaseEndsAt, string serverTime)
        {
            return new GameStateDto
            {
                gameId = "g-1",
                phase = GamePhases.Night,
                phaseVersion = phaseVersion,
                phaseEndsAt = phaseEndsAt,
                serverTime = serverTime
            };
        }

        [Test]
        public void 남은_시간은_서버가_준_차이를_로컬_시간에_붙여_계산한다()
        {
            var clock = new PhaseClock();
            // 서버 응답의 남은 시간 = 12:00:30.123 - 12:00:05.456 = 24.667초. 기기 시계와 상관없다.
            clock.Sync(GameJson.FromJson<GameStateDto>(GameJsonFixtures.GameState), 100.0);

            Assert.IsTrue(clock.HasDeadline);
            Assert.AreEqual(7L, clock.PhaseVersion);
            Assert.AreEqual(24.667, clock.RemainingSeconds(100.0), 0.001);
            Assert.AreEqual(20.0, clock.RemainingSeconds(104.667), 0.001);
        }

        [Test]
        public void 마감이_지나면_0이고_만료로_본다()
        {
            var clock = new PhaseClock();
            clock.Sync(State(1, "2026-10-01T12:00:10Z", "2026-10-01T12:00:00Z"), 0.0);

            Assert.IsFalse(clock.IsExpired(9.9));
            Assert.AreEqual(0.0, clock.RemainingSeconds(15.0));
            Assert.IsTrue(clock.IsExpired(15.0));
        }

        [Test]
        public void 표시용_초는_올림한다()
        {
            var clock = new PhaseClock();
            clock.Sync(State(1, "2026-10-01T12:00:10Z", "2026-10-01T12:00:00Z"), 0.0);

            Assert.AreEqual(10, clock.RemainingWholeSeconds(0.0));
            Assert.AreEqual(1, clock.RemainingWholeSeconds(9.8));
            Assert.AreEqual(0, clock.RemainingWholeSeconds(10.0));
        }

        [Test]
        public void 종료된_게임은_마감이_없다()
        {
            var clock = new PhaseClock();
            clock.Sync(GameJson.FromJson<GameStateDto>(GameJsonFixtures.GameStateEnded), 0.0);

            Assert.IsFalse(clock.HasDeadline);
            Assert.AreEqual(0.0, clock.RemainingSeconds(0.0));
            Assert.IsFalse(clock.IsExpired(100.0));
        }

        [Test]
        public void 동기화_전에는_마감이_없다()
        {
            var clock = new PhaseClock();

            Assert.IsFalse(clock.HasDeadline);
            Assert.AreEqual(-1L, clock.PhaseVersion);
            Assert.AreEqual(0, clock.RemainingWholeSeconds(0.0));
        }

        [Test]
        public void 늦게_도착한_옛_페이즈_응답은_무시한다()
        {
            var clock = new PhaseClock();
            clock.Sync(State(8, "2026-10-01T12:01:00Z", "2026-10-01T12:00:00Z"), 0.0);

            bool applied = clock.Sync(State(7, "2026-10-01T12:00:05Z", "2026-10-01T12:00:00Z"), 1.0);

            Assert.IsFalse(applied);
            Assert.AreEqual(8L, clock.PhaseVersion);
            Assert.AreEqual(59.0, clock.RemainingSeconds(1.0), 0.001);
        }

        [Test]
        public void 같은_페이즈에서는_가장_이른_마감_추정을_쓴다()
        {
            var clock = new PhaseClock();
            clock.Sync(State(3, "2026-10-01T12:00:30Z", "2026-10-01T12:00:00Z"), 0.0);   // 마감 추정 = 30.0

            // 지연이 커서 마감이 더 늦게 추정되는 응답은 반영하지 않는다
            clock.Sync(State(3, "2026-10-01T12:00:30Z", "2026-10-01T12:00:01Z"), 1.5);   // 추정 = 30.5
            Assert.AreEqual(20.0, clock.RemainingSeconds(10.0), 0.001);

            // 지연이 작아 더 이른 추정은 반영한다
            clock.Sync(State(3, "2026-10-01T12:00:30Z", "2026-10-01T12:00:02Z"), 1.8);   // 추정 = 29.8
            Assert.AreEqual(19.8, clock.RemainingSeconds(10.0), 0.001);
        }

        [Test]
        public void 새_페이즈가_되면_마감을_새로_잡는다()
        {
            var clock = new PhaseClock();
            clock.Sync(State(3, "2026-10-01T12:00:05Z", "2026-10-01T12:00:00Z"), 0.0);

            clock.Sync(State(4, "2026-10-01T12:01:05Z", "2026-10-01T12:00:05Z"), 5.0);

            Assert.AreEqual(4L, clock.PhaseVersion);
            Assert.AreEqual(60.0, clock.RemainingSeconds(5.0), 0.001);
        }
    }
}
