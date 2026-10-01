using System.Collections.Generic;
using NUnit.Framework;
using static WhoisntCitizen.Game.Tests.GameTestData;

namespace WhoisntCitizen.Game.Tests
{
    public class GameScreenTextTests
    {
        [Test]
        public void 상단_페이즈_표시()
        {
            Assert.AreEqual("2일차 밤", GameScreenText.PhaseTitle(2, GamePhases.Night));
            Assert.AreEqual("1일차 밤 결과", GameScreenText.PhaseTitle(1, GamePhases.NightResult));
            Assert.AreEqual("3일차 투표", GameScreenText.PhaseTitle(3, GamePhases.Vote));
            Assert.AreEqual("게임 종료", GameScreenText.PhaseTitle(3, GamePhases.Ended));
        }

        [TestCase(0, "00:00")]
        [TestCase(9, "00:09")]
        [TestCase(75, "01:15")]
        [TestCase(-3, "00:00")]
        public void 남은_시간_표시(int seconds, string expected)
        {
            Assert.AreEqual(expected, GameScreenText.Timer(seconds));
        }

        [Test]
        public void 페이즈_안내는_결과가_따로_오는_페이즈에서_비어_있다()
        {
            Assert.AreEqual("2일차 밤이 되었습니다.", GameScreenText.PhaseAnnouncement(new GameEvent { Phase = GamePhases.Night, Day = 2 }));
            Assert.AreEqual("1일차 낮이 되었습니다. 토론을 시작하세요.", GameScreenText.PhaseAnnouncement(new GameEvent { Phase = GamePhases.Day, Day = 1 }));
            Assert.AreEqual("투표 시간입니다. 하단 [+] 버튼으로 투표하세요.", GameScreenText.PhaseAnnouncement(new GameEvent { Phase = GamePhases.Vote, Day = 1 }));
            Assert.AreEqual(string.Empty, GameScreenText.PhaseAnnouncement(new GameEvent { Phase = GamePhases.NightResult, Day = 1 }));
            Assert.AreEqual(string.Empty, GameScreenText.PhaseAnnouncement(new GameEvent { Phase = GamePhases.Execution, Day = 1 }));
        }

        [Test]
        public void 직업_안내와_능력_줄()
        {
            var drunk = Me(11, RoleCodes.CrewDrunk, ActionCodes.ReadCorpseRole, 1);
            drunk.roleName = "주정뱅이";
            var raider = Me(11, RoleCodes.PirateRaider, ActionCodes.SelectAttackTarget, -1);
            var sailor = Me(11, RoleCodes.CrewSailor, null, -1);

            Assert.AreEqual("당신의 직업은 주정뱅이입니다.", GameScreenText.RoleAnnouncement(drunk));
            Assert.AreEqual("시체 확인 · 남은 횟수 1", GameScreenText.AbilityLine(drunk));
            Assert.AreEqual("공격", GameScreenText.AbilityLine(raider));
            Assert.AreEqual("밤에 쓰는 능력이 없습니다.", GameScreenText.AbilityLine(sailor));
        }

        [Test]
        public void 밤_능력_패널_안내()
        {
            var state = NightAllAlive();

            Assert.AreEqual("능력을 모두 사용했습니다.", GameScreenText.NightStatus(AbilityBlock.NoUsesLeft, ActionCodes.ReadCorpseRole, 0, false, state));
            Assert.AreEqual(GameScreenText.SkippedTonight, GameScreenText.NightStatus(AbilityBlock.None, ActionCodes.Protect, 0, true, state));
            Assert.AreEqual("보호 대상: 영희님\n다른 사람을 골라 바꿀 수 있습니다.", GameScreenText.NightStatus(AbilityBlock.None, ActionCodes.Protect, 12, false, state));
            Assert.AreEqual("보호 대상을 고르세요.", GameScreenText.NightStatus(AbilityBlock.None, ActionCodes.Protect, 0, false, state));
        }

        [Test]
        public void 접수_안내()
        {
            var state = NightAllAlive();
            var plain = new NightActionResultDto { accepted = true };
            var contact = new NightActionResultDto { accepted = true };
            contact.contactedPirateIds.Add(13);

            Assert.AreEqual("공격 대상으로 영희님을 골랐습니다.", GameScreenText.ActionAccepted(plain, ActionCodes.SelectAttackTarget, 12, false, state));
            Assert.AreEqual(GameScreenText.SkippedTonight, GameScreenText.ActionAccepted(plain, ActionCodes.SelectAttackTarget, 0, true, state));
            Assert.AreEqual("해적 민수님과 접선했습니다.", GameScreenText.ActionAccepted(contact, ActionCodes.WatchAction, 13, false, state));
            Assert.AreEqual("영희님에게 투표했습니다.", GameScreenText.VoteAccepted(12, state));
        }

        [Test]
        public void 승패_표시()
        {
            Assert.AreEqual("승리", GameScreenText.Outcome(Factions.Crew, Factions.Crew));
            Assert.AreEqual("패배", GameScreenText.Outcome(Factions.Pirate, Factions.Crew));
            Assert.AreEqual(string.Empty, GameScreenText.Outcome(null, Factions.Crew));
        }

        [Test]
        public void 득표_한_줄()
        {
            var executed = GameJson.FromJson<ExecutionResultDto>(GameJsonFixtures.ExecutionExecuted);
            var none = GameJson.FromJson<ExecutionResultDto>(GameJsonFixtures.ExecutionNoVotes);

            Assert.AreEqual("득표: 철수님 3표, 영희님 1표", GameScreenText.VoteSummary(executed));
            Assert.AreEqual(string.Empty, GameScreenText.VoteSummary(none));
        }

        [Test]
        public void 처형_득표를_투표_패널_형식으로()
        {
            var e = GameJson.FromJson<ExecutionResultDto>(GameJsonFixtures.ExecutionExecuted);

            Dictionary<long, int> counts = GameScreenText.VoteCounts(e);

            Assert.AreEqual(2, counts.Count);
            Assert.AreEqual(3, counts[11]);
            Assert.AreEqual(1, counts[12]);
        }

        [Test]
        public void 리치_텍스트를_막는다()
        {
            Assert.AreEqual("<​color=red>철수", GameScreenText.NoRichText("<color=red>철수"));
            Assert.AreEqual(string.Empty, GameScreenText.NoRichText(null));
        }
    }
}
