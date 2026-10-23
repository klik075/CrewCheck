using System.Collections.Generic;
using NUnit.Framework;
using static WhoisntCitizen.Game.Tests.GameTestData;

namespace WhoisntCitizen.Game.Tests
{
    public class PhaseTrackerTests
    {
        [Test]
        public void 처음_받은_상태는_페이즈_변경_하나만_만든다()
        {
            var tracker = new PhaseTracker();
            var first = State(GamePhases.Night, 7, P(11, "철수", true), P(12, "영희", false));

            List<GameEvent> events = tracker.Update(first);

            Assert.AreEqual(1, events.Count, "이미 죽어 있던 영희는 사망 이벤트로 알리지 않는다");
            Assert.AreEqual(GameEventType.PhaseChanged, events[0].Type);
            Assert.AreEqual(GamePhases.Night, events[0].Phase);
            Assert.IsNull(events[0].PreviousPhase);
            Assert.AreEqual(0, events[0].MissedPhases);
            Assert.AreSame(first, tracker.Current);
        }

        [Test]
        public void 밤_결과로_넘어가며_죽은_사람을_알린다()
        {
            var tracker = new PhaseTracker();
            tracker.Update(State(GamePhases.Night, 7, P(11, "철수", true), P(12, "영희", true), P(13, "민수", true)));

            List<GameEvent> events = tracker.Update(State(GamePhases.NightResult, 8, P(11, "철수", true), P(12, "영희", false), P(13, "민수", true)));

            Assert.AreEqual(2, events.Count);
            Assert.AreEqual(GameEventType.PhaseChanged, events[0].Type);
            Assert.AreEqual(GamePhases.Night, events[0].PreviousPhase);
            Assert.AreEqual(GamePhases.NightResult, events[0].Phase);
            Assert.AreEqual(0, events[0].MissedPhases);
            Assert.AreEqual(GameEventType.PlayerDied, events[1].Type);
            Assert.AreEqual(12L, events[1].PlayerId);
            Assert.AreEqual("영희", events[1].Nickname);
        }

        [Test]
        public void 같은_페이즈면_이벤트가_없다()
        {
            var tracker = new PhaseTracker();
            tracker.Update(State(GamePhases.Night, 7, P(11, "철수", true)));
            var again = State(GamePhases.Night, 7, P(11, "철수", true));

            Assert.AreEqual(0, tracker.Update(again).Count);
            Assert.AreSame(again, tracker.Current, "같은 페이즈의 최신 응답으로 바꿔 둔다");
        }

        [Test]
        public void 같은_페이즈_도중에_죽은_사람도_알린다()
        {
            var tracker = new PhaseTracker();
            tracker.Update(State(GamePhases.Vote, 10, P(11, "철수", true), P(12, "영희", true)));

            // 서버는 연결이 끊긴 플레이어를 페이즈를 바꾸지 않고 사망 처리한다
            List<GameEvent> events = tracker.Update(State(GamePhases.Vote, 10, P(11, "철수", true), P(12, "영희", false)));

            Assert.AreEqual(1, events.Count, "페이즈는 그대로라 사망 이벤트만 생긴다");
            Assert.AreEqual(GameEventType.PlayerDied, events[0].Type);
            Assert.AreEqual(12L, events[0].PlayerId);
            Assert.AreEqual("영희", events[0].Nickname);
        }

        [Test]
        public void 늦게_도착한_옛_응답은_무시한다()
        {
            var tracker = new PhaseTracker();
            var newer = State(GamePhases.Day, 9, P(11, "철수", true));
            tracker.Update(newer);

            List<GameEvent> events = tracker.Update(State(GamePhases.NightResult, 8, P(11, "철수", true)));

            Assert.AreEqual(0, events.Count);
            Assert.AreSame(newer, tracker.Current);
        }

        [Test]
        public void 폴링_사이에_지나간_페이즈_수를_알려준다()
        {
            var tracker = new PhaseTracker();
            tracker.Update(State(GamePhases.Night, 7, P(11, "철수", true)));

            List<GameEvent> events = tracker.Update(State(GamePhases.Day, 9, P(11, "철수", true)));

            Assert.AreEqual(GamePhases.Day, events[0].Phase);
            Assert.AreEqual(1, events[0].MissedPhases, "NIGHT_RESULT를 놓쳤다");
        }

        [Test]
        public void 게임이_끝나면_승리_진영을_알린다()
        {
            var tracker = new PhaseTracker();
            tracker.Update(State(GamePhases.Execution, 12, P(11, "철수", true), P(12, "영희", true)));
            var ended = State(GamePhases.Ended, 13, P(11, "철수", true), P(12, "영희", false));
            ended.winner = Factions.Crew;

            List<GameEvent> events = tracker.Update(ended);

            Assert.AreEqual(3, events.Count);
            Assert.AreEqual(GameEventType.PhaseChanged, events[0].Type);
            Assert.AreEqual(GameEventType.PlayerDied, events[1].Type);
            Assert.AreEqual(GameEventType.GameEnded, events[2].Type);
            Assert.AreEqual(Factions.Crew, events[2].Winner);
        }

        [Test]
        public void 처음_받은_상태가_종료면_종료도_알린다()
        {
            var tracker = new PhaseTracker();

            List<GameEvent> events = tracker.Update(GameJson.FromJson<GameStateDto>(GameJsonFixtures.GameStateEnded));

            Assert.AreEqual(2, events.Count);
            Assert.AreEqual(GameEventType.GameEnded, events[1].Type);
            Assert.AreEqual(Factions.Crew, events[1].Winner);
        }

        [Test]
        public void 다른_게임이면_처음부터_다시_본다()
        {
            var tracker = new PhaseTracker();
            tracker.Update(State(GamePhases.Vote, 20, P(11, "철수", true)));
            var other = State(GamePhases.Night, 1, P(11, "철수", true));
            other.gameId = "g-2";

            List<GameEvent> events = tracker.Update(other);

            Assert.AreEqual(1, events.Count);
            Assert.IsNull(events[0].PreviousPhase);
            Assert.AreSame(other, tracker.Current);
        }

        [Test]
        public void Reset하면_다음_상태를_처음_상태로_본다()
        {
            var tracker = new PhaseTracker();
            tracker.Update(State(GamePhases.Night, 7, P(11, "철수", true)));

            tracker.Reset();
            List<GameEvent> events = tracker.Update(State(GamePhases.Night, 7, P(11, "철수", true)));

            Assert.AreEqual(1, events.Count);
            Assert.IsNull(events[0].PreviousPhase);
        }

        [Test]
        public void null은_무시한다()
        {
            var tracker = new PhaseTracker();

            Assert.AreEqual(0, tracker.Update(null).Count);
            Assert.IsNull(tracker.Current);
        }
    }
}
