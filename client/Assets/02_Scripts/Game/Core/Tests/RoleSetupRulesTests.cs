using System.Collections.Generic;
using NUnit.Framework;

namespace WhoisntCitizen.Game.Tests
{
    public class RoleSetupRulesTests
    {
        // ---------------------------------------------------------------- 서버 GET /api/v1/role-setup/options와 같은 값

        private static RoleDto Role(string code, string name, string faction)
        {
            return new RoleDto { code = code, name = name, faction = faction };
        }

        private static RoleCompositionDto Comp(int playerCount, params string[] roles)
        {
            return new RoleCompositionDto { playerCount = playerCount, roles = new List<string>(roles) };
        }

        private const string R = RoleCodes.PirateRaider, P = RoleCodes.PirateParrot, C = RoleCodes.CrewCaptain,
            D = RoleCodes.CrewDoctor, L = RoleCodes.CrewLookout, M = RoleCodes.CrewMonkey, B = RoleCodes.CrewBoatswain,
            K = RoleCodes.CrewDrunk, S = RoleCodes.CrewSailor;

        private static RoleSetupOptionsDto Options()
        {
            return new RoleSetupOptionsDto
            {
                minPlayers = 4,
                maxPlayers = 12,
                roles = new List<RoleDto>
                {
                    Role(R, "해적", Factions.Pirate), Role(P, "앵무새", Factions.Pirate),
                    Role(C, "선장", Factions.Crew), Role(D, "선의", Factions.Crew), Role(L, "망루지기", Factions.Crew),
                    Role(M, "원숭이", Factions.Crew), Role(B, "갑판장", Factions.Crew), Role(K, "주정뱅이", Factions.Crew),
                    Role(S, "선원", Factions.Crew),
                },
                randomCandidates = new List<string> { P, C, D, L, M, B, K },
                recommended = new List<RoleCompositionDto>
                {
                    Comp(4, R, C, D, S),
                    Comp(5, R, C, D, S, S),
                    Comp(7, R, R, C, D, L, M, S),
                    Comp(9, R, R, P, C, D, L, B, M, K),
                    Comp(12, R, R, R, P, C, D, L, B, M, K, S, S),
                },
            };
        }

        private static RoleSetupDto Setup(string mode, params RoleCompositionDto[] custom)
        {
            return new RoleSetupDto
            {
                mode = mode,
                customCompositions = new List<RoleCompositionDto>(custom),
                randomCandidates = new List<string>(Options().randomCandidates),
            };
        }

        // ---------------------------------------------------------------- 정해진 구성

        [Test]
        public void 추천은_추천_구성이고_커스텀은_편집한_인원수만_바뀌고_랜덤은_정해진_구성이_없다()
        {
            RoleSetupOptionsDto options = Options();
            RoleSetupDto custom = Setup(RoleSetupModes.Custom, Comp(4, R, D, D, S));

            CollectionAssert.AreEqual(new[] { R, C, D, S }, RoleSetupRules.FixedComposition(Setup(RoleSetupModes.Recommended), options, 4));
            CollectionAssert.AreEqual(new[] { R, D, D, S }, RoleSetupRules.FixedComposition(custom, options, 4));
            CollectionAssert.AreEqual(new[] { R, C, D, S, S }, RoleSetupRules.FixedComposition(custom, options, 5));
            Assert.IsNull(RoleSetupRules.FixedComposition(Setup(RoleSetupModes.Random), options, 4));
        }

        [Test]
        public void 모드가_없으면_추천으로_본다()
        {
            CollectionAssert.AreEqual(new[] { R, C, D, S },
                RoleSetupRules.FixedComposition(new RoleSetupDto { mode = null }, Options(), 4));
            Assert.AreEqual(RoleSetupModes.Recommended, RoleSetupModes.Normalize(""));
        }

        [Test]
        public void 해적_진영_자리는_추천_구성의_해적_진영_수다()
        {
            Assert.AreEqual(1, RoleSetupRules.PirateSlots(Options(), 4));
            Assert.AreEqual(3, RoleSetupRules.PirateSlots(Options(), 9)); // 해적 2 + 앵무새
            Assert.AreEqual(4, RoleSetupRules.PirateSlots(Options(), 12));
        }

        // ---------------------------------------------------------------- 커스텀 편집

        [Test]
        public void 직업을_더하면_선원_자리가_바뀌고_빼면_선원으로_돌아간다()
        {
            RoleSetupOptionsDto options = Options();
            var five = new List<string> { R, C, D, S, S };

            List<string> added = RoleSetupRules.Add(options, five, L);
            CollectionAssert.AreEqual(new[] { R, C, D, L, S }, added);

            List<string> removed = RoleSetupRules.Remove(options, added, C);
            CollectionAssert.AreEqual(new[] { R, D, L, S, S }, removed);
        }

        [Test]
        public void 선원이_없으면_더할_수_없고_선원은_직접_바꿀_수_없다()
        {
            var nine = new List<string> { R, R, P, C, D, L, B, M, K };

            Assert.IsFalse(RoleSetupRules.CanAdd(nine, C));
            Assert.IsFalse(RoleSetupRules.CanAdd(new List<string> { R, S }, S));
            Assert.IsFalse(RoleSetupRules.CanRemove(new List<string> { R, S }, S));
            Assert.IsFalse(RoleSetupRules.CanRemove(nine, S));
            Assert.IsTrue(RoleSetupRules.CanRemove(nine, P));
        }

        [Test]
        public void 커스텀_표는_인원수_순으로_넣고_지우면_추천으로_돌아간다()
        {
            RoleSetupDto setup = Setup(RoleSetupModes.Custom, Comp(7, R, R, C, D, S, S, S));

            RoleSetupRules.SetCustom(setup, 4, new List<string> { R, D, D, S });
            RoleSetupRules.SetCustom(setup, 7, new List<string> { R, P, C, D, S, S, S });

            Assert.AreEqual(2, setup.customCompositions.Count);
            Assert.AreEqual(4, setup.customCompositions[0].playerCount);
            CollectionAssert.AreEqual(new[] { R, P, C, D, S, S, S }, setup.customCompositions[1].roles);

            RoleSetupRules.RemoveCustom(setup, 4);
            CollectionAssert.AreEqual(new[] { R, C, D, S }, RoleSetupRules.FixedComposition(setup, Options(), 4));
        }

        // ---------------------------------------------------------------- 검증 (서버와 같은 문장)

        [Test]
        public void 규칙에_맞으면_문제가_없다()
        {
            Assert.IsNull(RoleSetupRules.Problem(Options(), 4, new List<string> { R, D, D, S }));
        }

        [Test]
        public void 공격할_해적이_없으면_문제다()
        {
            StringAssert.Contains("공격할 수 있는 해적",
                RoleSetupRules.Problem(Options(), 4, new List<string> { P, C, D, S }));
        }

        [Test]
        public void 해적_진영이_선원_진영보다_적지_않으면_문제다()
        {
            Assert.AreEqual("해적 진영(2명)은 선원 진영(2명)보다 적어야 합니다.",
                RoleSetupRules.Problem(Options(), 4, new List<string> { R, P, C, S }));
        }

        [Test]
        public void 직업_수가_다르거나_모르는_직업이면_문제다()
        {
            StringAssert.Contains("직업 수(3)", RoleSetupRules.Problem(Options(), 4, new List<string> { R, C, S }));
            StringAssert.Contains("CREW_GHOST", RoleSetupRules.Problem(Options(), 4, new List<string> { R, C, S, "CREW_GHOST" }));
        }

        [Test]
        public void 커스텀_표의_첫_번째_문제를_인원수와_함께_알려_준다()
        {
            RoleSetupDto setup = Setup(RoleSetupModes.Custom, Comp(5, R, C, D, S, S), Comp(4, P, C, D, S));

            StringAssert.StartsWith("4인 구성: ", RoleSetupRules.FirstCustomProblem(setup, Options()));
        }

        // ---------------------------------------------------------------- 랜덤 후보

        [Test]
        public void 랜덤_후보는_선택지_순서로_유지하고_해적_선원은_항상_후보다()
        {
            RoleSetupOptionsDto options = Options();
            RoleSetupDto setup = Setup(RoleSetupModes.Random);
            setup.randomCandidates = new List<string>();

            RoleSetupRules.SetCandidate(setup, options, K, true);
            RoleSetupRules.SetCandidate(setup, options, C, true);
            RoleSetupRules.SetCandidate(setup, options, S, false);

            CollectionAssert.AreEqual(new[] { C, K }, setup.randomCandidates);
            Assert.IsTrue(RoleSetupRules.IsCandidate(setup, R));
            Assert.IsTrue(RoleSetupRules.IsCandidate(setup, S));
            Assert.IsFalse(RoleSetupRules.IsCandidate(setup, P));
        }

        // ---------------------------------------------------------------- 비교

        [Test]
        public void 구성_안의_순서만_다르면_같은_설정이다()
        {
            RoleSetupDto a = Setup(RoleSetupModes.Custom, Comp(4, R, C, D, S));
            RoleSetupDto b = Setup(RoleSetupModes.Custom, Comp(4, S, D, C, R));

            Assert.IsTrue(RoleSetupRules.SameSetup(a, b));
            b.mode = RoleSetupModes.Random;
            Assert.IsFalse(RoleSetupRules.SameSetup(a, b));
        }

        [Test]
        public void 복사본을_바꿔도_원본은_그대로다()
        {
            RoleSetupDto original = Setup(RoleSetupModes.Custom, Comp(4, R, C, D, S));
            RoleSetupDto copy = original.Clone();

            copy.customCompositions[0].roles[1] = D;
            copy.randomCandidates.Clear();

            Assert.AreEqual(C, original.customCompositions[0].roles[1]);
            Assert.AreEqual(7, original.randomCandidates.Count);
        }

        // ---------------------------------------------------------------- 화면 문장

        [Test]
        public void 구성은_직업별_개수로_보여_준다()
        {
            Assert.AreEqual("해적 2, 선장 1, 선의 1, 망루지기 1, 원숭이 1, 선원 1",
                RoleSetupRules.DescribeComposition(Options(), new List<string> { S, M, L, D, C, R, R }));
            Assert.AreEqual("해적 진영 2명 / 선원 진영 5명",
                RoleSetupRules.DescribeFactions(Options(), new List<string> { R, R, C, D, L, M, S }));
        }

        [Test]
        public void 랜덤은_진영별로_어떻게_뽑히는지_보여_준다()
        {
            RoleSetupOptionsDto options = Options();
            RoleSetupDto all = Setup(RoleSetupModes.Random);

            Assert.AreEqual("해적 진영 2명: 해적 1 + 1자리는 해적/앵무새 중 무작위\n선원 진영 5명: 후보 6개 중 5개 무작위",
                RoleSetupRules.DescribeRandom(all, options, 7));

            RoleSetupDto few = Setup(RoleSetupModes.Random);
            few.randomCandidates = new List<string> { C, D };
            Assert.AreEqual("해적 진영 2명: 해적 2\n선원 진영 5명: 후보 2개 전부 + 선원 3",
                RoleSetupRules.DescribeRandom(few, options, 7));
        }
    }
}
