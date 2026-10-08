using System.Linq;
using NUnit.Framework;

namespace WhoisntCitizen.Game.Tests
{
    /// <summary>
    /// GameSession을 FakeGameApi로 돌려 본다. 대부분 봇 행동을 끄고(BotsAct = false) 결과를 고정한다.
    /// 플레이어: 나 101, 철수 102 해적, 영희 103 앵무새, 민수 104 선장, 지훈 105 선의, 수진 106 망루지기, 현우 107 주정뱅이, 유나 108 선원.
    /// </summary>
    public class GameSessionTests
    {
        private double now;
        private FakeGameApi fake;
        private TestGameApi api;
        private RecordingView view;
        private GameSession session;

        private void Start(string myRole, bool botsAct)
        {
            now = 0;
            fake = new FakeGameApi(new FakeGameOptions { MyRole = myRole, BotsAct = botsAct }, () => now);
            api = new TestGameApi(fake);
            view = new RecordingView();
            session = new GameSession(api, FakeGameApi.FakeGameId, view, () => now, 1.0);
        }

        /// <summary>가짜 서버를 다음 페이즈로 넘기고, 1초 뒤 폴링한다.</summary>
        private void SkipAndPoll()
        {
            fake.SkipToNextPhase();
            now += 1;
            session.Tick();
        }

        private void SkipUntil(string phase)
        {
            for (int i = 0; i < 20 && session.State.phase != phase; i++)
            {
                SkipAndPoll();
            }
            Assert.AreEqual(phase, session.State.phase);
        }

        // ---------------------------------------------------------------- 폴링

        [Test]
        public void 첫_Tick에_상태와_내_역할을_보여준다()
        {
            Start(RoleCodes.CrewCaptain, false);

            session.Tick();

            Assert.AreEqual(GamePhases.Night, session.State.phase);
            Assert.AreEqual(1, view.Phases.Count);
            Assert.IsNull(view.Phases[0].PreviousPhase);
            Assert.AreEqual(RoleCodes.CrewCaptain, session.Me.role);
            Assert.AreEqual(1, view.MyRoles.Count);
            Assert.AreEqual(20, session.RemainingWholeSeconds);
            Assert.AreEqual(AbilityBlock.None, session.NightAbility);
        }

        [Test]
        public void 폴링_간격을_지킨다()
        {
            Start(RoleCodes.CrewSailor, false);

            session.Tick();
            now = 0.5;
            session.Tick();
            Assert.AreEqual(1, api.Calls("GetState"));

            now = 1.0;
            session.Tick();
            Assert.AreEqual(2, api.Calls("GetState"));
        }

        [Test]
        public void 응답을_기다리는_동안_폴링을_겹치지_않는다()
        {
            Start(RoleCodes.CrewSailor, false);
            api.Deferred = true;

            session.Tick();
            now = 5;
            session.Tick();
            Assert.AreEqual(1, api.Calls("GetState"), "앞선 응답이 오기 전에는 다시 보내지 않는다");

            api.Flush();
            session.Tick();
            Assert.AreEqual(2, api.Calls("GetState"));
        }

        [Test]
        public void 닫은_뒤에_온_응답은_무시한다()
        {
            Start(RoleCodes.CrewSailor, false);
            api.Deferred = true;
            session.Tick();

            session.Close();
            api.Flush();
            now = 5;
            session.Tick();

            Assert.AreEqual(0, view.TotalCalls);
            Assert.AreEqual(1, api.Calls("GetState"));
        }

        [Test]
        public void 페이즈가_바뀔_때마다_내_역할을_다시_받는다()
        {
            Start(RoleCodes.CrewDrunk, false);
            session.Tick();
            Assert.AreEqual(1, api.Calls("GetMe"));

            now = 1;
            session.Tick();
            Assert.AreEqual(1, api.Calls("GetMe"), "같은 페이즈면 다시 받지 않는다");

            SkipAndPoll();
            Assert.AreEqual(2, api.Calls("GetMe"));
        }

        // ---------------------------------------------------------------- 결과 받기

        [Test]
        public void 제출로_판정되면_바로_다시_폴링해서_밤_결과를_보여준다()
        {
            Start(RoleCodes.CrewCaptain, true);
            session.Tick();

            Assert.IsTrue(session.SubmitNightAction(102));
            Assert.AreEqual(GamePhases.NightResult, view.ActionsAccepted.Single().phase);
            session.Tick(); // 시간이 지나지 않았어도 바로 폴링한다

            Assert.AreEqual(GamePhases.NightResult, session.State.phase);
            NightResultDto night = view.NightResults.Single();
            Assert.AreEqual(1, night.day);
            Assert.AreEqual(Factions.Pirate, night.reports.Single().faction);
            Assert.AreSame(night, session.LastNightResult);
        }

        [Test]
        public void 놓친_밤_결과도_받아서_보여준다()
        {
            Start(RoleCodes.PirateRaider, false);
            session.Tick();
            session.SubmitNightAction(104);

            now = 26; // 20초에 NIGHT_RESULT, 25초에 DAY
            session.Tick();

            Assert.AreEqual(GamePhases.Day, view.Phases.Last().Phase);
            Assert.AreEqual(1, view.Phases.Last().MissedPhases);
            Assert.AreEqual(104L, view.NightResults.Single().killedPlayerId);
            Assert.AreEqual("민수", view.Deaths.Single().Nickname);
        }

        [Test]
        public void 놓친_처형_결과도_다음_밤에_받아서_보여준다()
        {
            Start(RoleCodes.CrewSailor, false);
            session.Tick();
            now = 41; // 40초에 VOTE
            session.Tick();
            Assert.IsTrue(session.Vote(105));
            Assert.AreEqual(105L, session.MyVoteTarget);

            now = 61; // 55초에 EXECUTION, 60초에 2일차 NIGHT
            session.Tick();

            Assert.AreEqual(GamePhases.Night, session.State.phase);
            ExecutionResultDto e = view.Executions.Single();
            Assert.AreEqual(1, e.day);
            Assert.AreEqual(105L, e.executedPlayerId);
            Assert.AreEqual(1, view.NightResults.Count, "1일차 밤 결과도 한 번만");
        }

        [Test]
        public void 해적이_밤에_이기면_밤_결과와_게임_결과를_보여주고_폴링을_멈춘다()
        {
            Start(RoleCodes.PirateRaider, false); // 해적 진영 3 : 선원 5
            session.Tick();
            SkipUntil(GamePhases.Vote);
            session.Vote(104);
            SkipUntil(GamePhases.Night);
            session.SubmitNightAction(105);
            SkipAndPoll();                         // 선원 3 → 해적 승, NIGHT → ENDED

            Assert.IsTrue(session.IsEnded);
            Assert.AreEqual(105L, view.NightResults.Last().killedPlayerId);
            Assert.AreEqual(2, view.NightResults.Last().day);
            Assert.AreEqual(1, view.Executions.Count, "2일차에는 투표 전에 끝나서 처형 결과가 없다");
            Assert.AreEqual(Factions.Pirate, view.GameResults.Single().winner);

            int polls = api.Calls("GetState");
            now += 10;
            session.Tick();
            Assert.AreEqual(polls, api.Calls("GetState"), "끝나고 다 보여 줬으면 폴링을 멈춘다");
        }

        [Test]
        public void 투표로_이기면_처형_결과와_게임_결과를_보여준다()
        {
            Start(RoleCodes.CrewCaptain, false);
            session.Tick();
            SkipUntil(GamePhases.Vote);
            session.Vote(102);
            SkipAndPoll();                         // 해적 처형 → 접선하지 못한 앵무새만 남음 → 선원 승, VOTE → ENDED

            Assert.IsTrue(session.IsEnded);
            Assert.IsFalse(session.IsCancelled);
            Assert.AreEqual(102L, view.Executions.Single().executedPlayerId);
            Assert.AreEqual(Factions.Crew, view.GameResults.Single().winner);
            Assert.AreSame(view.GameResults.Single(), session.Result);
        }

        [Test]
        public void 취소된_게임은_결과를_받고_취소로_표시한다()
        {
            Start(RoleCodes.CrewSailor, false);
            session.Tick();

            fake.CancelGame(EndReasons.CancelledAllDisconnected);
            now += 1;
            session.Tick();

            Assert.IsTrue(session.IsEnded);
            Assert.IsTrue(session.IsCancelled);
            Assert.IsTrue(view.GameResults.Single().IsCancelled);
            Assert.IsNull(view.GameResults.Single().winner);
        }

        // ---------------------------------------------------------------- 고른 사람이 도중에 나감 (연결 끊김)

        [Test]
        public void 투표한_사람이_투표_도중_나가면_내_표를_지우고_다시_투표하라고_알린다()
        {
            Start(RoleCodes.CrewCaptain, false);
            session.Tick();
            SkipUntil(GamePhases.Vote);
            Assert.IsTrue(session.Vote(104));
            Assert.AreEqual(104L, session.MyVoteTarget);

            fake.DisconnectPlayer(104);
            now += 1;
            session.Tick();

            Assert.AreEqual(GamePhases.Vote, session.State.phase);
            Assert.AreEqual(0L, session.MyVoteTarget);
            Assert.AreEqual(GameScreenText.VoteTargetGone("민수"), view.Errors.Last());
            Assert.AreEqual(104L, view.Deaths.Last().PlayerId);
        }

        [Test]
        public void 밤에_고른_대상이_도중에_나가면_선택을_지우고_다시_고르라고_알린다()
        {
            Start(RoleCodes.CrewCaptain, false);
            session.Tick();
            Assert.IsTrue(session.SubmitNightAction(104));
            Assert.AreEqual(104L, session.MyNightTarget);

            fake.DisconnectPlayer(104);
            now += 1;
            session.Tick();

            Assert.AreEqual(GamePhases.Night, session.State.phase);
            Assert.AreEqual(0L, session.MyNightTarget);
            Assert.IsFalse(session.SkippedTonight, "넘긴 것이 아니라 아직 고르지 않은 상태가 된다");
            Assert.AreEqual(GameScreenText.NightTargetGone("민수"), view.Errors.Last());
        }

        [Test]
        public void 접선한_해적이_나가면_앵무새의_고정이_풀려_다시_고를_수_있다()
        {
            Start(RoleCodes.PirateParrot, false);
            session.Tick();
            Assert.IsTrue(session.SubmitNightAction(102));
            Assert.IsTrue(session.LockedTonight);

            fake.DisconnectPlayer(102);
            now += 1;
            session.Tick();

            Assert.IsFalse(session.IsEnded, "접선한 앵무새가 살아 있으므로 게임이 이어진다");
            Assert.IsFalse(session.LockedTonight);
            Assert.AreEqual(AbilityBlock.None, session.NightAbility);
            Assert.IsTrue(session.SubmitNightAction(104));
        }

        [Test]
        public void 처형으로_죽은_사람은_선택_취소_안내를_하지_않는다()
        {
            Start(RoleCodes.CrewCaptain, false);
            session.Tick();
            SkipUntil(GamePhases.Vote);
            session.Vote(104);

            SkipAndPoll(); // 104 처형, 페이즈가 바뀌면서 죽는다

            Assert.AreEqual(GamePhases.Execution, session.State.phase);
            Assert.AreEqual(104L, view.Deaths.Last().PlayerId);
            Assert.IsFalse(view.Errors.Contains(GameScreenText.VoteTargetGone("민수")));
        }

        // ---------------------------------------------------------------- 내 입력

        [Test]
        public void 앵무새가_접선하면_그날_밤_행동이_잠긴다()
        {
            Start(RoleCodes.PirateParrot, false);
            session.Tick();

            Assert.IsTrue(session.SubmitNightAction(102));
            CollectionAssert.AreEqual(new long[] { 102 }, view.ActionsAccepted.Single().contactedPirateIds);
            Assert.IsTrue(session.LockedTonight);
            Assert.AreEqual(AbilityBlock.LockedByContact, session.NightAbility);

            Assert.IsFalse(session.SubmitNightAction(104));
            Assert.AreEqual("해적과 접선해서 오늘 밤 행동이 확정되었습니다.", view.Errors.Last());
            Assert.AreEqual(1, api.Calls("SubmitNightAction"), "잠긴 동안은 서버에 보내지 않는다");

            SkipUntil(GamePhases.Day);
            SkipUntil(GamePhases.Night);
            Assert.IsFalse(session.LockedTonight, "다음 밤에는 풀린다");
        }

        [Test]
        public void 서버가_거절하면_서버_메시지를_보여준다()
        {
            Start(RoleCodes.CrewDoctor, false);
            session.Tick();
            Assert.IsTrue(session.SubmitNightAction(101));
            SkipUntil(GamePhases.Day);
            SkipUntil(GamePhases.Night);

            Assert.IsTrue(session.SubmitNightAction(101), "보내기는 한다 (어젯밤 자기 보호 여부는 서버만 안다)");
            Assert.AreEqual("이틀 연속으로 자신을 보호할 수 없습니다.", view.Errors.Single());
            Assert.AreEqual(0L, session.MyNightTarget, "거절된 선택은 기억하지 않는다");
        }

        [Test]
        public void 쓸_수_없는_능력과_투표는_보내지_않는다()
        {
            Start(RoleCodes.CrewSailor, false);
            session.Tick();

            Assert.IsFalse(session.SubmitNightAction(102));
            Assert.IsFalse(session.SkipNightAction());
            Assert.IsFalse(session.Vote(102));

            CollectionAssert.AreEqual(new[]
            {
                "밤에 쓰는 능력이 없습니다.",
                "밤에 쓰는 능력이 없습니다.",
                GameSession.CannotVoteMessage
            }, view.Errors);
            Assert.AreEqual(0, api.Calls("SubmitNightAction") + api.Calls("SkipNightAction") + api.Calls("Vote"));
        }

        [Test]
        public void 이번_밤_선택을_기억하고_다음_페이즈에_지운다()
        {
            Start(RoleCodes.PirateRaider, false);
            session.Tick();

            session.SubmitNightAction(104);
            Assert.AreEqual(104L, session.MyNightTarget);
            Assert.IsFalse(session.SkippedTonight);

            session.SkipNightAction();
            Assert.AreEqual(0L, session.MyNightTarget);
            Assert.IsTrue(session.SkippedTonight);

            SkipAndPoll();
            Assert.IsFalse(session.SkippedTonight);
            Assert.AreEqual(0L, session.MyNightTarget);
        }

        [Test]
        public void 낮_토론_넘기기를_기억하고_다음_페이즈에_지운다()
        {
            Start(RoleCodes.CrewSailor, false);
            session.Tick();

            Assert.IsFalse(session.CanSkipDay, "밤에는 넘길 수 없다");
            Assert.IsFalse(session.SkipDay());
            Assert.AreEqual(GameSession.CannotSkipDayMessage, view.Errors.Last());
            Assert.AreEqual(0, api.Calls("SkipDay"), "넘길 수 없으면 서버에 보내지 않는다");

            SkipUntil(GamePhases.Day);
            Assert.IsTrue(session.CanSkipDay);
            Assert.IsTrue(session.SkipDay());

            Assert.IsTrue(session.SkippedToday);
            Assert.IsFalse(session.CanSkipDay, "한 번 넘기면 다시 누를 수 없다");
            Assert.AreEqual(1L, session.DaySkipProgress.skippedCount);
            Assert.AreEqual(8L, session.DaySkipProgress.requiredCount);
            Assert.AreEqual(1, view.DaySkipsAccepted.Count);

            SkipAndPoll();
            Assert.AreEqual(GamePhases.Vote, session.State.phase);
            Assert.IsFalse(session.SkippedToday);
            Assert.IsNull(session.DaySkipProgress);
        }

        [Test]
        public void 응답을_기다리는_동안_다른_입력은_무시한다()
        {
            Start(RoleCodes.PirateRaider, false);
            session.Tick();
            api.Deferred = true;

            Assert.IsTrue(session.SubmitNightAction(104));
            Assert.IsFalse(session.SubmitNightAction(105));
            Assert.AreEqual(1, api.Calls("SubmitNightAction"));

            api.Flush();
            Assert.AreEqual(104L, session.MyNightTarget);
        }

        // ---------------------------------------------------------------- 연결 · 종료

        [Test]
        public void 연결이_끊기면_한_번만_알리고_다시_이어지면_알린다()
        {
            Start(RoleCodes.CrewSailor, false);
            session.Tick();

            api.Offline = true;
            now = 1;
            session.Tick();
            now = 2;
            session.Tick();
            CollectionAssert.AreEqual(new[] { false }, view.Connections);
            Assert.IsFalse(session.IsConnected);

            api.Offline = false;
            now = 3;
            session.Tick();
            CollectionAssert.AreEqual(new[] { false, true }, view.Connections);
            Assert.IsTrue(session.IsConnected);
        }

        [Test]
        public void 서버에서_게임이_지워지면_닫고_더_묻지_않는다()
        {
            Start(RoleCodes.CrewSailor, false);
            api.Gone = true;

            session.Tick();
            now = 5;
            session.Tick();

            Assert.IsTrue(view.Closed);
            Assert.IsTrue(session.IsClosed);
            Assert.AreEqual(1, api.Calls("GetState"));
        }
    }
}
