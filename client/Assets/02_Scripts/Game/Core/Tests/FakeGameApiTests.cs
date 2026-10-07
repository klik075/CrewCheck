using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace WhoisntCitizen.Game.Tests
{
    /// <summary>
    /// FakeGameApi가 서버와 같은 흐름·판정·오류 메시지를 내는지 확인한다.
    /// 결과를 고정하려고 대부분 BotsAct = false로 돌린다. 플레이어: 나 101, 철수 102 해적, 영희 103 앵무새,
    /// 민수 104 선장, 지훈 105 선의, 수진 106 망루지기, 현우 107 주정뱅이, 유나 108 선원.
    /// </summary>
    public class FakeGameApiTests
    {
        private const string Id = FakeGameApi.FakeGameId;
        private double now;

        private FakeGameApi Create(string myRole, bool botsAct)
        {
            now = 0;
            return new FakeGameApi(new FakeGameOptions { MyRole = myRole, BotsAct = botsAct }, () => now);
        }

        private static GameApiResult<T> Call<T>(Action<Action<GameApiResult<T>>> call) where T : class
        {
            GameApiResult<T> result = null;
            int calls = 0;
            call(r => { result = r; calls++; });
            Assert.AreEqual(1, calls, "콜백은 호출 안에서 정확히 한 번 불린다");
            return result;
        }

        private static GameStateDto State(FakeGameApi api)
        {
            return Call<GameStateDto>(cb => api.GetState(Id, cb)).Data;
        }

        private static MyRoleDto Me(FakeGameApi api)
        {
            return Call<MyRoleDto>(cb => api.GetMe(Id, cb)).Data;
        }

        private static GameApiResult<NightActionResultDto> Act(FakeGameApi api, long targetId)
        {
            return Call<NightActionResultDto>(cb => api.SubmitNightAction(Id, targetId, cb));
        }

        private static GameApiResult<VoteResultDto> VoteFor(FakeGameApi api, long targetId)
        {
            return Call<VoteResultDto>(cb => api.Vote(Id, targetId, true, cb));
        }

        private static NightResultDto NightResult(FakeGameApi api)
        {
            return Call<NightResultDto>(cb => api.GetNightResult(Id, cb)).Data;
        }

        private static ExecutionResultDto ExecutionResult(FakeGameApi api)
        {
            return Call<ExecutionResultDto>(cb => api.GetExecutionResult(Id, cb)).Data;
        }

        private static void SkipTo(FakeGameApi api, string phase)
        {
            for (int i = 0; i < 20 && State(api).phase != phase; i++)
            {
                api.SkipToNextPhase();
            }
            Assert.AreEqual(phase, State(api).phase);
        }

        private static void AssertRule<T>(GameApiResult<T> result, string message) where T : class
        {
            Assert.IsFalse(result.Success);
            Assert.AreEqual(409L, result.StatusCode);
            Assert.AreEqual(GameErrorCodes.GameRuleViolation, result.ErrorCode);
            Assert.AreEqual(message, result.Message);
        }

        // ---------------------------------------------------------------- 시작 · 설정

        [Test]
        public void 시작하면_1일차_밤이다()
        {
            var api = Create(RoleCodes.PirateRaider, false);

            GameStateDto s = State(api);

            Assert.AreEqual(Id, s.gameId);
            Assert.AreEqual(GamePhases.Night, s.phase);
            Assert.AreEqual(1, s.day);
            Assert.AreEqual(1L, s.phaseVersion);
            Assert.AreEqual(8, s.players.Count);
            Assert.IsTrue(s.players.All(p => p.alive));
            Assert.AreEqual(FakeGameApi.MyPlayerId, s.players[0].playerId);
            Assert.AreEqual("나", s.players[0].nickname);

            var clock = new PhaseClock();
            clock.Sync(s, 0);
            Assert.AreEqual(20.0, clock.RemainingSeconds(0), 0.001, "phaseEndsAt - serverTime = 밤 20초");
        }

        [Test]
        public void 해적은_처음부터_동료_해적을_안다()
        {
            MyRoleDto me = Me(Create(RoleCodes.PirateRaider, false));

            Assert.AreEqual(RoleCodes.PirateRaider, me.role);
            Assert.AreEqual("해적", me.roleName);
            Assert.AreEqual(Factions.Pirate, me.faction);
            Assert.AreEqual(ActionCodes.SelectAttackTarget, me.actionCode);
            Assert.AreEqual(-1, me.remainingUses);
            CollectionAssert.AreEqual(new long[] { 102 }, me.mafiaTeammateIds, "앵무새는 접선 전이라 모른다");
            Assert.IsFalse(me.contacted);
        }

        [Test]
        public void 원숭이는_위장_직업으로_보인다()
        {
            now = 0;
            var api = new FakeGameApi(new FakeGameOptions { MyRole = RoleCodes.CrewMonkey, MonkeyDisguise = RoleCodes.CrewCaptain, BotsAct = false }, () => now);

            MyRoleDto me = Me(api);

            Assert.AreEqual(RoleCodes.CrewCaptain, me.role);
            Assert.AreEqual("선장", me.roleName);
            Assert.AreEqual(ActionCodes.InvestigateFaction, me.actionCode);
        }

        [Test]
        public void 원숭이는_가짜_결과를_받는다()
        {
            now = 0;
            var api = new FakeGameApi(new FakeGameOptions { MyRole = RoleCodes.CrewMonkey, MonkeyDisguise = RoleCodes.CrewCaptain, BotsAct = false }, () => now);

            Assert.IsTrue(Act(api, 104).Success);
            api.SkipToNextPhase();
            ReportDto report = NightResult(api).reports.Single();

            Assert.AreEqual(ReportTypes.Faction, report.type);
            Assert.AreEqual(104L, report.targetId);
            CollectionAssert.Contains(new[] { Factions.Crew, Factions.Pirate }, report.faction, "진짜 결과와 같은 형식의 무작위 진영");
        }

        [Test]
        public void 잘못된_설정은_예외()
        {
            Assert.Throws<ArgumentNullException>(() => new FakeGameApi(null, null));
            Assert.Throws<ArgumentException>(() => new FakeGameApi(new FakeGameOptions { MyRole = "CREW_NEW" }, () => 0));
            Assert.Throws<ArgumentException>(() => new FakeGameApi(new FakeGameOptions { MyRole = RoleCodes.CrewMonkey, MonkeyDisguise = RoleCodes.CrewSailor }, () => 0));
            Assert.Throws<ArgumentException>(() => new FakeGameApi(new FakeGameOptions { NightSeconds = 0 }, () => 0));
        }

        [Test]
        public void 다른_게임은_404()
        {
            var r = Call<GameStateDto>(cb => Create(RoleCodes.CrewSailor, false).GetState("no-such-game", cb));

            Assert.IsFalse(r.Success);
            Assert.IsTrue(r.IsNotFound);
            Assert.AreEqual(GameErrorCodes.GameNotFound, r.ErrorCode);
            Assert.AreEqual("게임을 찾을 수 없습니다: no-such-game", r.Message);
        }

        // ---------------------------------------------------------------- 시간 · 페이즈

        [Test]
        public void 시간이_지나면_페이즈가_넘어간다()
        {
            var api = Create(RoleCodes.CrewSailor, false);

            now = 20;
            Assert.AreEqual(GamePhases.NightResult, State(api).phase);
            now = 25;
            Assert.AreEqual(GamePhases.Day, State(api).phase);
            now = 40;
            Assert.AreEqual(GamePhases.Vote, State(api).phase);
            now = 55;
            Assert.AreEqual(GamePhases.Execution, State(api).phase);
            now = 60;
            GameStateDto s = State(api);
            Assert.AreEqual(GamePhases.Night, s.phase);
            Assert.AreEqual(2, s.day);
            Assert.AreEqual(6L, s.phaseVersion);
        }

        [Test]
        public void 오래_멈췄다가_불러도_지난_페이즈를_모두_처리한다()
        {
            var api = Create(RoleCodes.CrewSailor, false);

            now = 125; // 60초 주기 두 번 + 5초
            GameStateDto s = State(api);

            Assert.AreEqual(GamePhases.Night, s.phase);
            Assert.AreEqual(3, s.day);
            Assert.AreEqual(11L, s.phaseVersion);
        }

        [Test]
        public void 봇이_있으면_내가_제출하는_순간_판정된다()
        {
            var api = Create(RoleCodes.CrewCaptain, true);

            var r = Act(api, 102);

            Assert.IsTrue(r.Success);
            Assert.AreEqual(GamePhases.NightResult, r.Data.phase);
            Assert.AreEqual(2L, r.Data.phaseVersion);
            ReportDto report = NightResult(api).reports.Single();
            Assert.AreEqual(ReportTypes.Faction, report.type);
            Assert.AreEqual(Factions.Pirate, report.faction);
        }

        [Test]
        public void 내가_할_일이_없으면_봇만으로_바로_판정된다()
        {
            var api = Create(RoleCodes.CrewSailor, true);

            GameStateDto s = State(api);

            Assert.AreEqual(GamePhases.NightResult, s.phase);
            Assert.AreEqual(1, NightResult(api).day);
        }

        // ---------------------------------------------------------------- 밤 능력

        [Test]
        public void 해적_공격으로_죽고_밤_결과에_나온다()
        {
            var api = Create(RoleCodes.PirateRaider, false);

            var r = Act(api, 104);
            Assert.IsTrue(r.Success);
            Assert.AreEqual(GamePhases.Night, r.Data.phase, "봇이 아직 내지 않아 시간 종료까지 기다린다");
            api.SkipToNextPhase();

            NightResultDto n = NightResult(api);
            Assert.AreEqual(1, n.day);
            Assert.AreEqual(104L, n.killedPlayerId);
            Assert.AreEqual("민수", n.killedNickname);
            Assert.IsFalse(n.protectedByDoctor);
            Assert.AreEqual(0, n.reports.Count, "공격은 개인 결과가 없다");
            Assert.IsFalse(GameStateQueries.IsAlive(State(api), 104));
        }

        [Test]
        public void 서버와_같은_검증_메시지()
        {
            var raider = Create(RoleCodes.PirateRaider, false);
            AssertRule(Act(raider, 101), "자신을 대상으로 할 수 없는 능력입니다.");
            AssertRule(Act(raider, 999), "이 게임에 참가하지 않은 플레이어입니다: 999");
            AssertRule(VoteFor(raider, 104), "현재 페이즈(NIGHT)에서는 할 수 없는 요청입니다. 필요 페이즈: VOTE");
            SkipTo(raider, GamePhases.Day);
            AssertRule(Act(raider, 104), "현재 페이즈(DAY)에서는 할 수 없는 요청입니다. 필요 페이즈: NIGHT");

            var sailor = Create(RoleCodes.CrewSailor, false);
            AssertRule(Act(sailor, 102), "밤에 사용할 능력이 없는 직업입니다.");
            AssertRule(Call<NightActionResultDto>(cb => sailor.SkipNightAction(Id, cb)), "밤에 사용할 능력이 없는 직업입니다.");
        }

        [Test]
        public void 선의는_이틀_연속_자기_보호를_못_한다()
        {
            var api = Create(RoleCodes.CrewDoctor, false);
            Assert.IsTrue(Act(api, 101).Success);
            api.SkipToNextPhase();
            SkipTo(api, GamePhases.Night);

            AssertRule(Act(api, 101), "이틀 연속으로 자신을 보호할 수 없습니다.");
            Assert.IsTrue(Act(api, 102).Success, "다른 사람은 보호할 수 있다");
            api.SkipToNextPhase();
            SkipTo(api, GamePhases.Night);
            Assert.IsTrue(Act(api, 101).Success, "하루 쉬면 다시 자기 보호 가능");
        }

        [Test]
        public void 선장은_대상의_진영을_본다()
        {
            var api = Create(RoleCodes.CrewCaptain, false);

            Act(api, 103);
            api.SkipToNextPhase();
            ReportDto report = NightResult(api).reports.Single();

            Assert.AreEqual(ReportTypes.Faction, report.type);
            Assert.AreEqual(103L, report.targetId);
            Assert.AreEqual("영희", report.targetNickname);
            Assert.AreEqual(Factions.Pirate, report.faction, "앵무새는 해적 진영");
        }

        [Test]
        public void 앵무새는_해적을_지목하면_바로_접선하고_그날_밤_잠긴다()
        {
            var api = Create(RoleCodes.PirateParrot, false);

            var r = Act(api, 102);
            Assert.IsTrue(r.Success);
            CollectionAssert.AreEqual(new long[] { 102 }, r.Data.contactedPirateIds);
            Assert.AreEqual(GamePhases.Night, r.Data.phase);

            AssertRule(Act(api, 104), "이번 밤 행동이 이미 확정되었습니다.");
            AssertRule(Call<NightActionResultDto>(cb => api.SkipNightAction(Id, cb)), "이번 밤 행동이 이미 확정되었습니다.");

            MyRoleDto me = Me(api);
            Assert.IsTrue(me.contacted);
            CollectionAssert.AreEqual(new long[] { 102 }, me.mafiaTeammateIds);

            api.SkipToNextPhase();
            Assert.AreEqual(0, NightResult(api).reports.Count, "해적을 관찰하면 행동 결과 대신 접선");

            SkipTo(api, GamePhases.Night);
            var next = Act(api, 104);
            Assert.IsTrue(next.Success, "다음 밤에는 잠금이 풀린다");
            Assert.AreEqual(0, next.Data.contactedPirateIds.Count);
        }

        [Test]
        public void 앵무새는_해적이_아닌_사람의_행동을_본다()
        {
            var api = Create(RoleCodes.PirateParrot, false);

            Act(api, 104);
            api.SkipToNextPhase();
            ReportDto report = NightResult(api).reports.Single();

            Assert.AreEqual(ReportTypes.Actions, report.type);
            Assert.AreEqual(104L, report.targetId);
            Assert.AreEqual(0, report.actions.Count, "봇이 행동하지 않았다");
        }

        [Test]
        public void 망루지기는_방문자를_본다()
        {
            var api = Create(RoleCodes.CrewLookout, false);

            Act(api, 104);
            api.SkipToNextPhase();
            ReportDto report = NightResult(api).reports.Single();

            Assert.AreEqual(ReportTypes.Visitors, report.type);
            Assert.AreEqual(104L, report.targetId);
            Assert.AreEqual(0, report.players.Count, "자기 자신(망루지기)은 방문자에서 빠진다");
        }

        [Test]
        public void 주정뱅이는_시체가_있어야_하고_쓸_때마다_횟수가_준다()
        {
            var api = Create(RoleCodes.CrewDrunk, false);
            Assert.AreEqual(2, Me(api).remainingUses);
            AssertRule(Act(api, 104), "사망한 플레이어만 대상으로 할 수 있는 능력입니다.");

            SkipTo(api, GamePhases.Vote);
            VoteFor(api, 104);
            SkipTo(api, GamePhases.Night);
            Assert.IsTrue(Act(api, 104).Success);
            api.SkipToNextPhase();

            ReportDto report = NightResult(api).reports.Single();
            Assert.AreEqual(ReportTypes.CorpseRole, report.type);
            Assert.AreEqual(RoleCodes.CrewCaptain, report.roleCode);
            Assert.AreEqual("선장", report.roleName);
            Assert.AreEqual(1, Me(api).remainingUses);
        }

        // ---------------------------------------------------------------- 투표 · 처형

        [Test]
        public void 다시_투표하면_덮어쓰고_최다_득표자를_처형한다()
        {
            var api = Create(RoleCodes.CrewSailor, false);
            SkipTo(api, GamePhases.Vote);

            Assert.AreEqual(GamePhases.Vote, VoteFor(api, 104).Data.phase);
            VoteFor(api, 105);
            api.SkipToNextPhase();

            ExecutionResultDto e = ExecutionResult(api);
            Assert.AreEqual(105L, e.executedPlayerId);
            Assert.AreEqual("지훈", e.executedNickname);
            Assert.IsFalse(e.tie);
            Assert.AreEqual(1, e.votes.Count);
            Assert.AreEqual(105L, e.votes[0].playerId);
            Assert.AreEqual(1, e.votes[0].count);

            SkipTo(api, GamePhases.Vote);
            AssertRule(VoteFor(api, 105), "투표 대상가 이미 사망했습니다: 105");
        }

        [Test]
        public void 투표가_없으면_처형도_없다()
        {
            var api = Create(RoleCodes.CrewSailor, false);
            SkipTo(api, GamePhases.Execution);

            ExecutionResultDto e = ExecutionResult(api);
            Assert.IsFalse(e.HasExecution);
            Assert.IsFalse(e.tie);
            Assert.AreEqual(0, e.votes.Count);
        }

        [Test]
        public void 봇이_있으면_내가_투표하는_순간_판정된다()
        {
            var api = Create(RoleCodes.PirateRaider, true); // 봇 해적은 나를 공격하지 않는다
            Act(api, 104);
            SkipTo(api, GamePhases.Vote);

            var r = VoteFor(api, 106);

            Assert.IsTrue(r.Success);
            Assert.AreNotEqual(GamePhases.Vote, r.Data.phase, "전원 투표 → 바로 처형 판정 (승패가 나면 ENDED)");
            Assert.AreEqual(1, ExecutionResult(api).day);
        }

        // ---------------------------------------------------------------- 승패

        [Test]
        public void 해적이_밤에_이기면_밤_결과_없이_바로_끝난다()
        {
            var api = Create(RoleCodes.PirateRaider, false); // 해적 진영 3 : 선원 5
            api.SkipToNextPhase();                           // 1일차 밤: 공격 안 함
            SkipTo(api, GamePhases.Vote);
            VoteFor(api, 104);                               // 선원 4
            SkipTo(api, GamePhases.Night);
            Act(api, 105);
            long versionBefore = State(api).phaseVersion;
            api.SkipToNextPhase();                           // 선원 3 → 3 >= 3 해적 승

            GameStateDto s = State(api);
            Assert.AreEqual(GamePhases.Ended, s.phase);
            Assert.AreEqual(versionBefore + 1, s.phaseVersion, "NIGHT_RESULT를 거치지 않는다");
            Assert.AreEqual(Factions.Pirate, s.winner);
            Assert.IsNull(s.phaseEndsAt);
            Assert.AreEqual(105L, NightResult(api).killedPlayerId, "마지막 밤 결과는 그대로 조회된다");

            GameResultDto g = Call<GameResultDto>(cb => api.GetResult(Id, cb)).Data;
            Assert.IsTrue(g.ended);
            Assert.AreEqual(Factions.Pirate, g.winner);
            Assert.AreEqual(2, g.lastDay);
            Assert.AreEqual(8, g.players.Count);
            Assert.AreEqual(RoleCodes.PirateParrot, g.players.Single(p => p.playerId == 103).role);

            AssertRule(Act(api, 106), "현재 페이즈(ENDED)에서는 할 수 없는 요청입니다. 필요 페이즈: NIGHT");
        }

        [Test]
        public void 선원이_투표로_이기면_처형_결과_없이_바로_끝난다()
        {
            var api = Create(RoleCodes.CrewCaptain, false);
            SkipTo(api, GamePhases.Vote);
            VoteFor(api, 102);
            api.SkipToNextPhase();                           // 해적 처형 → 접선하지 못한 앵무새만 남음 → 선원 승

            GameStateDto s = State(api);
            Assert.AreEqual(GamePhases.Ended, s.phase);
            Assert.AreEqual(Factions.Crew, s.winner);
            Assert.AreEqual(EndReasons.Win, s.endReason);
            Assert.AreEqual(102L, ExecutionResult(api).executedPlayerId);
        }

        [Test]
        public void 접선한_앵무새가_살아_있으면_해적이_처형돼도_게임이_이어진다()
        {
            var api = Create(RoleCodes.PirateParrot, false);
            Assert.IsTrue(Act(api, 102).Success);            // 해적과 접선
            SkipTo(api, GamePhases.Vote);
            VoteFor(api, 102);
            api.SkipToNextPhase();

            GameStateDto s = State(api);
            Assert.AreEqual(GamePhases.Execution, s.phase);
            Assert.IsNull(s.winner);
            Assert.IsNull(s.endReason);
        }

        // ---------------------------------------------------------------- 연결 끊김 · 취소 (개발용)

        [Test]
        public void 연결이_끊긴_사람은_죽고_그_사람의_표와_그_사람이_받은_표가_빠진다()
        {
            var api = Create(RoleCodes.CrewCaptain, false);
            SkipTo(api, GamePhases.Vote);
            VoteFor(api, 105);

            api.DisconnectPlayer(105);

            GameStateDto s = State(api);
            Assert.IsFalse(s.players.Single(p => p.playerId == 105).alive);
            Assert.AreEqual(GamePhases.Vote, s.phase, "남은 사람이 아직 다 내지 않았으므로 투표가 이어진다");
            Assert.IsTrue(VoteFor(api, 104).Success, "다시 투표할 수 있다");
        }

        [Test]
        public void 해적이_끊기면_접선하지_못한_앵무새만_남아_선원이_이긴다()
        {
            var api = Create(RoleCodes.CrewCaptain, false);

            api.DisconnectPlayer(102);

            GameStateDto s = State(api);
            Assert.AreEqual(GamePhases.Ended, s.phase);
            Assert.AreEqual(Factions.Crew, s.winner);
        }

        [Test]
        public void 접선한_대상이_끊기면_앵무새의_고정이_풀려_다시_고를_수_있다()
        {
            var api = Create(RoleCodes.PirateParrot, false);
            Assert.IsTrue(Act(api, 102).Success);            // 접선 → 이번 밤 행동 고정
            AssertRule(Act(api, 104), "이번 밤 행동이 이미 확정되었습니다.");

            api.DisconnectPlayer(102);

            Assert.AreEqual(GamePhases.Night, State(api).phase);
            Assert.IsTrue(Act(api, 104).Success);
        }

        [Test]
        public void 게임을_취소하면_승리_팀_없이_끝나고_결과에_취소_이유가_온다()
        {
            var api = Create(RoleCodes.CrewSailor, false);

            api.CancelGame(EndReasons.CancelledNoDeaths);

            GameStateDto s = State(api);
            Assert.AreEqual(GamePhases.Ended, s.phase);
            Assert.IsNull(s.winner);
            Assert.AreEqual(EndReasons.CancelledNoDeaths, s.endReason);
            GameResultDto g = Call<GameResultDto>(cb => api.GetResult(Id, cb)).Data;
            Assert.IsTrue(g.ended);
            Assert.IsTrue(g.IsCancelled);
            Assert.AreEqual(8, g.players.Count);
        }

        [Test]
        public void 끝나기_전에는_결과와_직업이_비공개다()
        {
            var api = Create(RoleCodes.CrewSailor, false);

            GameResultDto g = Call<GameResultDto>(cb => api.GetResult(Id, cb)).Data;
            Assert.IsFalse(g.ended);
            Assert.IsTrue(string.IsNullOrEmpty(g.winner));
            Assert.AreEqual(0, g.players.Count);
            Assert.AreEqual(1, g.lastDay);

            AssertRule(Call<NightResultDto>(cb => api.GetNightResult(Id, cb)), "아직 공개된 밤 결과가 없습니다.");
            AssertRule(Call<ExecutionResultDto>(cb => api.GetExecutionResult(Id, cb)), "아직 처형 결과가 없습니다.");
        }

        // ---------------------------------------------------------------- D와 함께 쓰기

        [Test]
        public void PhaseTracker와_AbilityRules가_가짜_서버_응답으로_동작한다()
        {
            var api = Create(RoleCodes.PirateRaider, false);
            var tracker = new PhaseTracker();
            tracker.Update(State(api));

            Assert.AreEqual(AbilityBlock.None, AbilityRules.CheckNightAbility(State(api), Me(api), false));
            CollectionAssert.DoesNotContain(AbilityRules.NightTargets(State(api), Me(api)).Select(p => p.playerId).ToList(), 101L);

            Act(api, 104);
            api.SkipToNextPhase();
            List<GameEvent> events = tracker.Update(State(api));

            Assert.AreEqual(2, events.Count);
            Assert.AreEqual(GamePhases.NightResult, events[0].Phase);
            Assert.AreEqual(GameEventType.PlayerDied, events[1].Type);
            Assert.AreEqual("민수", events[1].Nickname);
            Assert.AreEqual("민수님이 밤사이 사망했습니다.", ReportFormatter.NightSummary(NightResult(api)));
        }
    }
}
