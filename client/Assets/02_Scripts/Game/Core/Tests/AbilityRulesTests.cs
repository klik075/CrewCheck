using System.Linq;
using System.Reflection;
using NUnit.Framework;
using static WhoisntCitizen.Game.Tests.GameTestData;

namespace WhoisntCitizen.Game.Tests
{
    public class AbilityRulesTests
    {
        // ---------------------------------------------------------------- 규칙표

        [Test]
        public void 모든_능력_코드에_규칙이_있다()
        {
            // 서버에 능력이 추가되면 ActionCodes 상수와 함께 규칙도 넣어야 한다.
            foreach (FieldInfo f in typeof(ActionCodes).GetFields(BindingFlags.Public | BindingFlags.Static).Where(x => x.IsLiteral))
            {
                Assert.IsNotNull(AbilityRules.Find((string)f.GetRawConstantValue()), f.Name);
            }
        }

        // 서버 ActionCode enum(b41b546)과 같은 값: (코드, livingTarget, selfTargetAllowed, maxUses)
        [TestCase(ActionCodes.InvestigateFaction, true, false, -1)]
        [TestCase(ActionCodes.Protect, true, true, -1)]
        [TestCase(ActionCodes.WatchVisitors, true, true, -1)]
        [TestCase(ActionCodes.Block, true, false, -1)]
        [TestCase(ActionCodes.ReadCorpseRole, false, false, 2)]
        [TestCase(ActionCodes.SelectAttackTarget, true, false, -1)]
        [TestCase(ActionCodes.WatchAction, true, false, -1)]
        [TestCase(ActionCodes.BanVote, true, false, -1)]
        [TestCase(ActionCodes.Seduce, true, false, -1)]
        [TestCase(ActionCodes.KrakenMark, true, false, -1)]
        [TestCase(ActionCodes.KrakenStrike, false, false, -1)]
        public void 규칙은_서버_ActionCode와_같다(string code, bool livingTarget, bool selfAllowed, int maxUses)
        {
            ActionRule rule = AbilityRules.Find(code);

            Assert.AreEqual(livingTarget, rule.LivingTarget);
            Assert.AreEqual(selfAllowed, rule.SelfAllowed);
            Assert.AreEqual(maxUses, rule.MaxUses);
            Assert.AreEqual(code != ActionCodes.KrakenStrike, rule.NeedsTarget); // 크라켄 발동만 대상이 없다
        }

        // ---------------------------------------------------------------- 중립 직업

        [Test]
        public void 세이렌은_밤에_유혹할_수_있다()
        {
            var me = Me(11, "NEUTRAL_SIREN", ActionCodes.Seduce, -1);

            Assert.AreEqual(AbilityBlock.None, AbilityRules.CheckNightAbility(NightAllAlive(), me, false));
            CollectionAssert.AreEqual(new long[] { 12, 13 }, AbilityRules.NightTargets(NightAllAlive(), me).Select(p => p.playerId));
        }

        [Test]
        public void 세이렌은_유혹에_성공한_다음_밤에_쉰다()
        {
            var me = Me(11, "NEUTRAL_SIREN", ActionCodes.Seduce, -1);
            me.abilityAvailable = false;

            Assert.AreEqual(AbilityBlock.Resting, AbilityRules.CheckNightAbility(NightAllAlive(), me, false));
        }

        [Test]
        public void 서버가_못_쓴다고_해도_더_정확한_이유를_먼저_알린다()
        {
            var drunk = Me(11, RoleCodes.CrewDrunk, ActionCodes.ReadCorpseRole, 2);
            drunk.abilityAvailable = false; // 사망자가 없어서

            Assert.AreEqual(AbilityBlock.NoCorpse, AbilityRules.CheckNightAbility(NightAllAlive(), drunk, false));
        }

        [Test]
        public void 크라켄은_이미_표식을_남긴_사람에게_다시_남길_수_없다()
        {
            var me = Me(11, "NEUTRAL_KRAKEN", ActionCodes.KrakenMark, -1);
            me.krakenMarkIds.Add(12);

            CollectionAssert.AreEqual(new long[] { 13 }, AbilityRules.NightTargets(NightAllAlive(), me).Select(p => p.playerId));
            Assert.IsFalse(AbilityRules.IsValidNightTarget(NightAllAlive(), me, 12));
            Assert.IsTrue(AbilityRules.IsValidNightTarget(NightAllAlive(), me, 13));
        }

        [Test]
        public void 크라켄은_표식을_남긴_생존자가_있어야_발동할_수_있다()
        {
            var me = Me(11, "NEUTRAL_KRAKEN", ActionCodes.KrakenMark, -1);
            Assert.IsFalse(AbilityRules.CanStrike(NightAllAlive(), me, AbilityBlock.None));

            me.krakenMarkIds.Add(14); // /me 이후에 죽었다 (state가 더 새롭다)
            Assert.IsFalse(AbilityRules.CanStrike(NightWithCorpse(), me, AbilityBlock.None));

            me.krakenMarkIds.Add(12);
            CollectionAssert.AreEqual(new long[] { 12 }, AbilityRules.KrakenMarks(NightWithCorpse(), me).Select(p => p.playerId));
            Assert.IsTrue(AbilityRules.CanStrike(NightWithCorpse(), me, AbilityBlock.None));
            Assert.IsFalse(AbilityRules.CanStrike(NightWithCorpse(), me, AbilityBlock.Dead));
        }

        [Test]
        public void 크라켄이_아니면_표식_목록이_비어_있다()
        {
            var me = Me(11, RoleCodes.PirateRaider, ActionCodes.SelectAttackTarget, -1);
            me.krakenMarkIds.Add(12);

            Assert.AreEqual(0, AbilityRules.KrakenMarks(NightAllAlive(), me).Count);
        }

        [Test]
        public void 모르는_코드는_규칙이_없다()
        {
            Assert.IsNull(AbilityRules.Find("TIME_TRAVEL"));
            Assert.IsNull(AbilityRules.Find(null));
        }

        // ---------------------------------------------------------------- 밤 능력 사용 가능 여부

        [Test]
        public void 요리사의_투표_금지는_그_밤_결과와_같은_날에만_간다()
        {
            var vote = State(GamePhases.Vote, 12, P(11, "철수", true), P(12, "영희", true));
            vote.day = 2;
            var banned = new NightResultDto { day = 2 };
            banned.reports.Add(new ReportDto { type = ReportTypes.VoteBanned });
            var yesterday = new NightResultDto { day = 1 };
            yesterday.reports.Add(new ReportDto { type = ReportTypes.VoteBanned });
            var other = new NightResultDto { day = 2 };
            other.reports.Add(new ReportDto { type = ReportTypes.VoteBan, targetId = 12 }); // 내가 요리사로 남을 막았다

            Assert.IsTrue(AbilityRules.IsVoteBanned(vote, banned));
            Assert.IsFalse(AbilityRules.IsVoteBanned(vote, yesterday));
            Assert.IsFalse(AbilityRules.IsVoteBanned(vote, other));
            Assert.IsFalse(AbilityRules.IsVoteBanned(vote, null));
        }

        [Test]
        public void 해적은_밤에_능력을_쓸_수_있다()
        {
            var me = Me(11, RoleCodes.PirateRaider, ActionCodes.SelectAttackTarget, -1);

            Assert.AreEqual(AbilityBlock.None, AbilityRules.CheckNightAbility(NightAllAlive(), me, false));
        }

        [Test]
        public void 밤이_아니면_쓸_수_없다()
        {
            var me = Me(11, RoleCodes.PirateRaider, ActionCodes.SelectAttackTarget, -1);
            var day = State(GamePhases.Day, 9, P(11, "철수", true), P(12, "영희", true));

            Assert.AreEqual(AbilityBlock.NotNight, AbilityRules.CheckNightAbility(day, me, false));
        }

        [Test]
        public void 상태에서_사망했으면_me가_살아_있어도_쓸_수_없다()
        {
            // /me는 페이즈마다 한 번 받고 state는 매초 받으므로 생존 여부는 state를 따른다.
            var me = Me(11, RoleCodes.CrewCaptain, ActionCodes.InvestigateFaction, -1);
            var state = State(GamePhases.Night, 7, P(11, "철수", false), P(12, "영희", true));

            Assert.AreEqual(AbilityBlock.Dead, AbilityRules.CheckNightAbility(state, me, false));
        }

        [Test]
        public void 접선한_밤에는_앵무새_행동이_잠긴다()
        {
            var me = Me(11, RoleCodes.PirateParrot, ActionCodes.WatchAction, -1);

            Assert.AreEqual(AbilityBlock.LockedByContact, AbilityRules.CheckNightAbility(NightAllAlive(), me, true));
            Assert.AreEqual(AbilityBlock.None, AbilityRules.CheckNightAbility(NightAllAlive(), me, false), "다음 밤에는 풀린다");
        }

        [Test]
        public void 능력이_없는_직업()
        {
            var me = Me(11, RoleCodes.CrewSailor, null, -1);

            Assert.AreEqual(AbilityBlock.NoAbility, AbilityRules.CheckNightAbility(NightAllAlive(), me, false));
        }

        [Test]
        public void 모르는_능력_코드()
        {
            var me = Me(11, "CREW_NEW", "TIME_TRAVEL", -1);

            Assert.AreEqual(AbilityBlock.UnknownAction, AbilityRules.CheckNightAbility(NightAllAlive(), me, false));
        }

        [Test]
        public void 남은_횟수가_0이면_쓸_수_없다()
        {
            var me = Me(11, RoleCodes.CrewDrunk, ActionCodes.ReadCorpseRole, 0);

            Assert.AreEqual(AbilityBlock.NoUsesLeft, AbilityRules.CheckNightAbility(NightWithCorpse(), me, false));
        }

        [Test]
        public void 주정뱅이는_사망자가_있어야_쓸_수_있다()
        {
            var me = Me(11, RoleCodes.CrewDrunk, ActionCodes.ReadCorpseRole, 2);

            Assert.AreEqual(AbilityBlock.NoCorpse, AbilityRules.CheckNightAbility(NightAllAlive(), me, false));
            Assert.AreEqual(AbilityBlock.None, AbilityRules.CheckNightAbility(NightWithCorpse(), me, false));
        }

        // ---------------------------------------------------------------- 밤 능력 대상

        [Test]
        public void 해적은_자신을_빼고_동료를_포함한_생존자를_고른다()
        {
            var me = Me(11, RoleCodes.PirateRaider, ActionCodes.SelectAttackTarget, -1);
            me.mafiaTeammateIds.Add(13);

            var ids = AbilityRules.NightTargets(NightWithCorpse(), me).Select(p => p.playerId).ToArray();

            CollectionAssert.AreEqual(new long[] { 12, 13 }, ids, "자신(11)과 사망자(14) 제외, 동료(13)는 포함");
        }

        [Test]
        public void 선의는_자기_자신도_고를_수_있다()
        {
            var me = Me(11, RoleCodes.CrewDoctor, ActionCodes.Protect, -1);

            var ids = AbilityRules.NightTargets(NightWithCorpse(), me).Select(p => p.playerId).ToArray();

            CollectionAssert.AreEqual(new long[] { 11, 12, 13 }, ids);
        }

        [Test]
        public void 주정뱅이는_사망자만_고른다()
        {
            var me = Me(11, RoleCodes.CrewDrunk, ActionCodes.ReadCorpseRole, 2);

            var ids = AbilityRules.NightTargets(NightWithCorpse(), me).Select(p => p.playerId).ToArray();

            CollectionAssert.AreEqual(new long[] { 14 }, ids);
        }

        [Test]
        public void 능력이_없으면_대상도_없다()
        {
            var me = Me(11, RoleCodes.CrewSailor, null, -1);

            Assert.AreEqual(0, AbilityRules.NightTargets(NightWithCorpse(), me).Count);
        }

        [Test]
        public void 대상_하나씩_확인()
        {
            var me = Me(11, RoleCodes.CrewCaptain, ActionCodes.InvestigateFaction, -1);
            var state = NightWithCorpse();

            Assert.IsTrue(AbilityRules.IsValidNightTarget(state, me, 12));
            Assert.IsFalse(AbilityRules.IsValidNightTarget(state, me, 11), "자기 자신");
            Assert.IsFalse(AbilityRules.IsValidNightTarget(state, me, 14), "사망자");
            Assert.IsFalse(AbilityRules.IsValidNightTarget(state, me, 99), "참가자가 아님");
        }

        // ---------------------------------------------------------------- 투표

        [Test]
        public void 투표는_VOTE_페이즈의_생존자만_한다()
        {
            var vote = State(GamePhases.Vote, 11, P(11, "철수", true), P(12, "영희", false));
            var day = State(GamePhases.Day, 10, P(11, "철수", true));

            Assert.IsTrue(AbilityRules.CanVote(vote, 11));
            Assert.IsFalse(AbilityRules.CanVote(vote, 12), "사망자");
            Assert.IsFalse(AbilityRules.CanVote(vote, 99), "참가자가 아님");
            Assert.IsFalse(AbilityRules.CanVote(day, 11), "VOTE가 아님");
        }

        [Test]
        public void 투표_대상은_자신을_포함한_생존자다()
        {
            var vote = State(GamePhases.Vote, 11, P(11, "철수", true), P(12, "영희", false), P(13, "민수", true));

            var ids = AbilityRules.VoteTargets(vote).Select(p => p.playerId).ToArray();

            CollectionAssert.AreEqual(new long[] { 11, 13 }, ids);
        }
    }
}
