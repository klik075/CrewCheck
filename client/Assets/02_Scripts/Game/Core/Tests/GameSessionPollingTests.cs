using NUnit.Framework;

namespace WhoisntCitizen.Game.Tests
{
    /// <summary>
    /// 실시간 신호 연동용 폴링 제어: RequestPollNow(지금 바로 GET), SetPollInterval(간격 바꾸기).
    /// </summary>
    public class GameSessionPollingTests
    {
        private double now;
        private FakeGameApi fake;
        private TestGameApi api;
        private RecordingView view;
        private GameSession session;

        private void Start(double interval = 1.0)
        {
            now = 0;
            fake = new FakeGameApi(new FakeGameOptions { MyRole = RoleCodes.CrewSailor, BotsAct = false }, () => now);
            api = new TestGameApi(fake);
            view = new RecordingView();
            session = new GameSession(api, FakeGameApi.FakeGameId, view, () => now, interval);
        }

        private int Polls => api.Calls("GetState");

        // ---------------------------------------------------------------- RequestPollNow

        [Test]
        public void RequestPollNow하면_간격을_기다리지_않고_다음_Tick에_가져온다()
        {
            Start();
            session.Tick();          // 0초: 첫 폴링
            Assert.AreEqual(1, Polls);

            now = 0.3;
            session.Tick();
            Assert.AreEqual(1, Polls); // 간격(1초) 전

            session.RequestPollNow();
            session.Tick();
            Assert.AreEqual(2, Polls); // 0.3초에 바로
        }

        [Test]
        public void 신호로_가져온_뒤에는_다시_간격을_지킨다()
        {
            Start();
            session.Tick();
            now = 0.3;
            session.RequestPollNow();
            session.Tick();          // 2번째 (0.3초)

            now = 1.0;
            session.Tick();
            Assert.AreEqual(2, Polls); // 0.3 + 1 = 1.3초 전
            now = 1.3;
            session.Tick();
            Assert.AreEqual(3, Polls);
        }

        [Test]
        public void 응답을_기다리는_중에_요청하면_응답이_온_뒤_한_번_더_가져온다()
        {
            Start();
            api.Deferred = true;
            session.Tick();                 // 요청 보냄, 응답 대기
            Assert.AreEqual(1, Polls);

            session.RequestPollNow();       // 이 응답은 신호보다 먼저 출발했을 수 있다
            session.Tick();
            Assert.AreEqual(1, Polls);      // 동시에 두 번 보내지는 않는다

            api.Flush();                    // 첫 응답 도착
            session.Tick();
            Assert.AreEqual(2, Polls);      // 간격과 상관없이 바로 한 번 더
        }

        [Test]
        public void 기다리는_중에_여러_번_요청해도_추가_요청은_한_번이다()
        {
            Start();
            api.Deferred = true;
            session.Tick();
            session.RequestPollNow();
            session.RequestPollNow();
            session.RequestPollNow();

            api.Flush();
            session.Tick();
            api.Flush();
            session.Tick();                 // 0초 그대로 → 간격 전이라 더 가져오지 않는다
            Assert.AreEqual(2, Polls);
        }

        [Test]
        public void 닫은_뒤에는_RequestPollNow가_아무것도_하지_않는다()
        {
            Start();
            session.Tick();
            session.Close();
            session.RequestPollNow();
            session.Tick();
            Assert.AreEqual(1, Polls);
        }

        // ---------------------------------------------------------------- SetPollInterval

        [Test]
        public void 처음_간격은_생성자_값이다()
        {
            Start(2.0);
            Assert.AreEqual(2.0, session.PollIntervalSeconds);
        }

        [Test]
        public void 간격을_15초로_늘리면_1초마다_가져오지_않는다()
        {
            Start();
            session.Tick();                 // 0초
            session.SetPollInterval(15);

            now = 1;
            session.Tick();
            now = 14.9;
            session.Tick();
            Assert.AreEqual(1, Polls);

            now = 15;
            session.Tick();
            Assert.AreEqual(2, Polls);
        }

        [Test]
        public void 간격을_15초에서_1초로_줄이면_마지막_폴링_1초_뒤에_가져온다()
        {
            Start(15);
            session.Tick();                 // 0초
            now = 5;
            session.Tick();
            Assert.AreEqual(1, Polls);      // 15초 간격이라 아직

            session.SetPollInterval(1);     // 끊김 → 빠른 폴링
            session.Tick();
            Assert.AreEqual(2, Polls);      // 0 + 1 = 1초가 이미 지났다 → 바로
        }

        [Test]
        public void 간격을_줄여도_마지막_폴링에서_새_간격이_안_지났으면_기다린다()
        {
            Start(15);
            session.Tick();                 // 0초
            now = 0.4;
            session.SetPollInterval(1);
            session.Tick();
            Assert.AreEqual(1, Polls);
            now = 1.0;
            session.Tick();
            Assert.AreEqual(2, Polls);
        }

        [Test]
        public void 첫_폴링_전에_간격을_바꿔도_첫_Tick에는_바로_가져온다()
        {
            Start();
            session.SetPollInterval(15);
            session.Tick();
            Assert.AreEqual(1, Polls);
        }

        [TestCase(0.0)]
        [TestCase(-3.0)]
        [TestCase(0.05)]
        [TestCase(double.NaN)]
        public void 너무_짧거나_잘못된_간격은_0점2초로_올린다(double seconds)
        {
            Start();
            session.SetPollInterval(seconds);
            Assert.AreEqual(0.2, session.PollIntervalSeconds);
        }

        [Test]
        public void 신호와_간격_조절을_함께_쓰는_흐름()
        {
            Start();
            session.Tick();                 // 0초, 연결 전 1초 폴링
            session.SetPollInterval(15);    // 구독됨 → 안전망

            now = 3;
            session.RequestPollNow();       // game.changed
            session.Tick();
            Assert.AreEqual(2, Polls);

            now = 10;
            session.Tick();
            Assert.AreEqual(2, Polls);      // 3 + 15 = 18초 전

            session.SetPollInterval(1);     // 끊김
            session.Tick();
            Assert.AreEqual(3, Polls);      // 3 + 1 = 4초 이미 지남 → 바로
        }
    }
}
