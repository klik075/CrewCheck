using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using static WhoisntCitizen.Game.Tests.GameTestData;

namespace WhoisntCitizen.Game.Tests
{
    public class ReportFormatterTests
    {
        // ---------------------------------------------------------------- 밤 결과

        [Test]
        public void 밤_요약_사망자가_있으면_이름을_알린다()
        {
            var r = GameJson.FromJson<NightResultDto>(GameJsonFixtures.NightResultKilledFaction);

            Assert.AreEqual("영희님이 밤사이 사망했습니다.", ReportFormatter.NightSummary(r));
        }

        [Test]
        public void 밤_요약_선의가_살렸으면_보호를_알린다()
        {
            var r = GameJson.FromJson<NightResultDto>(GameJsonFixtures.NightResultSavedAllTypes);

            Assert.AreEqual("선의의 보호로 아무도 죽지 않았습니다.", ReportFormatter.NightSummary(r));
        }

        [Test]
        public void 밤_요약_아무_일도_없으면()
        {
            var r = GameJson.FromJson<NightResultDto>(GameJsonFixtures.NightResultEmpty);

            Assert.AreEqual("밤사이 아무도 죽지 않았습니다.", ReportFormatter.NightSummary(r));
            Assert.AreEqual(0, ReportFormatter.NightReports(r).Count);
        }

        [Test]
        public void 선장_조사_결과()
        {
            var r = GameJson.FromJson<NightResultDto>(GameJsonFixtures.NightResultKilledFaction);

            CollectionAssert.AreEqual(new[] { "철수님을 조사했습니다. 철수님은 해적 진영입니다." }, ReportFormatter.NightReports(r));
        }

        [Test]
        public void 망루지기_앵무새_주정뱅이_결과()
        {
            var r = GameJson.FromJson<NightResultDto>(GameJsonFixtures.NightResultSavedAllTypes);

            CollectionAssert.AreEqual(new[]
            {
                "영희님을 감시했습니다. 영희님을 찾아온 사람: 철수님, 민수님",
                "민수님을 관찰했습니다.\n민수님이 영희님을 보호했습니다.",
                "지훈님의 시체를 확인했습니다. 지훈님의 직업은 원숭이입니다."
            }, ReportFormatter.NightReports(r));
        }

        [Test]
        public void 방문자와_행동이_없을_때()
        {
            var visitors = new ReportDto { type = ReportTypes.Visitors, targetId = 12, targetNickname = "영희" };
            var actions = new ReportDto { type = ReportTypes.Actions, targetId = 13, targetNickname = "민수" };

            Assert.AreEqual("영희님을 감시했습니다. 영희님을 찾아온 사람이 없습니다.", ReportFormatter.Report(visitors));
            Assert.AreEqual("민수님을 관찰했습니다. 민수님은 아무 행동도 하지 않았습니다.", ReportFormatter.Report(actions));
        }

        [Test]
        public void 갑판장_차단_결과와_차단당한_사람_안내()
        {
            var block = new ReportDto { type = ReportTypes.Block, targetId = 12, targetNickname = "영희" };
            var blocked = new ReportDto { type = ReportTypes.Blocked };

            Assert.AreEqual("영희님을 차단했습니다. 영희님은 이번 밤 능력을 사용할 수 없습니다.", ReportFormatter.Report(block));
            Assert.AreEqual("갑판장에 의해 차단되어 이번 밤 능력을 사용할 수 없었습니다.", ReportFormatter.Report(blocked));
        }

        [Test]
        public void 앵무새는_행동별로_한_줄씩_본다()
        {
            var report = new ReportDto { type = ReportTypes.Actions, targetId = 11, targetNickname = "철수" };
            report.actions.Add(new ActionViewDto { actionCode = ActionCodes.SelectAttackTarget, targetId = 12, targetNickname = "영희" });
            report.actions.Add(new ActionViewDto { actionCode = ActionCodes.ReadCorpseRole, targetId = 14, targetNickname = "지훈" });

            Assert.AreEqual("철수님을 관찰했습니다.\n철수님이 영희님을 공격 대상으로 골랐습니다.\n철수님이 지훈님의 시체를 확인했습니다.", ReportFormatter.Report(report));
        }

        [Test]
        public void 모르는_결과_종류는_건너뛴다()
        {
            var r = new NightResultDto();
            r.reports.Add(new ReportDto { type = "FUTURE_TYPE", targetId = 12, targetNickname = "영희" });

            Assert.AreEqual(string.Empty, ReportFormatter.Report(r.reports[0]));
            Assert.AreEqual(0, ReportFormatter.NightReports(r).Count);
        }

        [Test]
        public void 닉네임이_비어_있으면_플레이어_번호로_쓴다()
        {
            var report = new ReportDto { type = ReportTypes.Faction, targetId = 12, faction = Factions.Crew };

            Assert.AreEqual("플레이어 12님을 조사했습니다. 플레이어 12님은 선원 진영입니다.", ReportFormatter.Report(report));
        }

        // ---------------------------------------------------------------- 접선 · 동료

        [Test]
        public void 접선_알림은_상태의_닉네임을_쓴다()
        {
            var state = NightAllAlive();

            Assert.AreEqual("해적 철수님, 민수님과 접선했습니다.", ReportFormatter.ContactMessage(state, new List<long> { 11, 13 }));
            Assert.AreEqual(string.Empty, ReportFormatter.ContactMessage(state, new List<long>()));
        }

        [Test]
        public void 동료_해적_줄()
        {
            var me = GameJson.FromJson<MyRoleDto>(GameJsonFixtures.MyRoleParrotContacted);
            var state = NightAllAlive();

            Assert.AreEqual("해적 동료: 철수님, 민수님", ReportFormatter.TeammatesLine(state, me));
            Assert.AreEqual(string.Empty, ReportFormatter.TeammatesLine(state, GameJson.FromJson<MyRoleDto>(GameJsonFixtures.MyRoleSailor)));
        }

        // ---------------------------------------------------------------- 처형 · 게임 결과

        [Test]
        public void 처형_요약()
        {
            var executed = GameJson.FromJson<ExecutionResultDto>(GameJsonFixtures.ExecutionExecuted);
            var tie = GameJson.FromJson<ExecutionResultDto>(GameJsonFixtures.ExecutionTie);
            var none = GameJson.FromJson<ExecutionResultDto>(GameJsonFixtures.ExecutionNoVotes);

            Assert.AreEqual("철수님이 처형되었습니다.", ReportFormatter.ExecutionSummary(executed));
            Assert.AreEqual("동점이라 아무도 처형되지 않았습니다.", ReportFormatter.ExecutionSummary(tie));
            Assert.AreEqual("투표가 없어 아무도 처형되지 않았습니다.", ReportFormatter.ExecutionSummary(none));
        }

        [Test]
        public void 득표_줄()
        {
            var executed = GameJson.FromJson<ExecutionResultDto>(GameJsonFixtures.ExecutionExecuted);

            CollectionAssert.AreEqual(new[] { "철수님 3표", "영희님 1표" }, ReportFormatter.VoteLines(executed));
        }

        [Test]
        public void 게임_결과_줄()
        {
            var g = GameJson.FromJson<GameResultDto>(GameJsonFixtures.GameResult);

            Assert.AreEqual("선원 진영 승리", ReportFormatter.WinnerLine(g.winner));
            Assert.AreEqual("해적 진영 승리", ReportFormatter.WinnerLine(Factions.Pirate));
            Assert.AreEqual(string.Empty, ReportFormatter.WinnerLine(null));
            CollectionAssert.AreEqual(new[] { "철수님 · 해적 · 사망", "지훈님 · 원숭이 · 생존" }, ReportFormatter.ResultLines(g));
        }

        // ---------------------------------------------------------------- 이름 · 안내 문구

        [Test]
        public void 모든_능력에_이름과_행동_문장이_있다()
        {
            foreach (FieldInfo f in typeof(ActionCodes).GetFields(BindingFlags.Public | BindingFlags.Static).Where(x => x.IsLiteral))
            {
                string code = (string)f.GetRawConstantValue();
                Assert.AreNotEqual("능력", ReportFormatter.ActionName(code), f.Name);

                var report = new ReportDto { type = ReportTypes.Actions, targetId = 11, targetNickname = "철수" };
                report.actions.Add(new ActionViewDto { actionCode = code, targetId = 12, targetNickname = "영희" });
                StringAssert.DoesNotContain("능력을 사용했습니다", ReportFormatter.Report(report), f.Name);
            }
        }

        [Test]
        public void 막힌_이유마다_안내_문구가_있다()
        {
            foreach (AbilityBlock block in Enum.GetValues(typeof(AbilityBlock)))
            {
                string message = ReportFormatter.AbilityBlockMessage(block);
                if (block == AbilityBlock.None)
                {
                    Assert.AreEqual(string.Empty, message);
                }
                else
                {
                    Assert.IsFalse(string.IsNullOrEmpty(message), block.ToString());
                }
            }
        }

        [Test]
        public void 진영_이름()
        {
            Assert.AreEqual("선원 진영", ReportFormatter.FactionName(Factions.Crew));
            Assert.AreEqual("해적 진영", ReportFormatter.FactionName(Factions.Pirate));
        }
    }
}
