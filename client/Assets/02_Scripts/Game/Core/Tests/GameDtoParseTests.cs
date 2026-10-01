using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;

namespace WhoisntCitizen.Game.Tests
{
    /// <summary>서버 실제 응답(GameJsonFixtures)을 JsonUtility로 읽었을 때 DTO에 제대로 들어가는지 확인한다.</summary>
    public class GameDtoParseTests
    {
        // ---------------------------------------------------------------- 게임 상태

        [Test]
        public void 게임_상태_기본_필드()
        {
            var s = GameJson.FromJson<GameStateDto>(GameJsonFixtures.GameState);

            Assert.AreEqual("3f2b9c1e-7d4a-4b8e-9a1f-2c6d8e0b5a71", s.gameId);
            Assert.AreEqual(GamePhases.Night, s.phase);
            Assert.AreEqual(2, s.day);
            Assert.AreEqual(7L, s.phaseVersion);
            Assert.IsTrue(string.IsNullOrEmpty(s.winner), "진행 중에는 winner가 없다");
        }

        [Test]
        public void 게임_상태_플레이어_목록()
        {
            var s = GameJson.FromJson<GameStateDto>(GameJsonFixtures.GameState);

            Assert.AreEqual(2, s.players.Count);
            Assert.AreEqual(11L, s.players[0].playerId);
            Assert.AreEqual("철수", s.players[0].nickname);
            Assert.IsTrue(s.players[0].alive);
            Assert.IsFalse(s.players[1].alive);
        }

        [Test]
        public void 남은_시간은_서버_시각_기준으로_계산한다()
        {
            var s = GameJson.FromJson<GameStateDto>(GameJsonFixtures.GameState);

            DateTime? endsAt = GameJson.ParseServerTime(s.phaseEndsAt);
            DateTime? now = GameJson.ParseServerTime(s.serverTime);

            Assert.IsTrue(endsAt.HasValue && now.HasValue);
            Assert.AreEqual(DateTimeKind.Utc, endsAt.Value.Kind);
            Assert.AreEqual(new DateTime(2026, 10, 1, 12, 0, 30, 123, DateTimeKind.Utc), endsAt.Value);
            Assert.AreEqual(24667, (endsAt.Value - now.Value).TotalMilliseconds, 0.5);
        }

        [Test]
        public void 종료된_게임은_winner가_있고_phaseEndsAt이_없다()
        {
            var s = GameJson.FromJson<GameStateDto>(GameJsonFixtures.GameStateEnded);

            Assert.AreEqual(GamePhases.Ended, s.phase);
            Assert.AreEqual(Factions.Crew, s.winner);
            Assert.IsNull(GameJson.ParseServerTime(s.phaseEndsAt));
            Assert.AreEqual(new DateTime(2026, 10, 1, 12, 10, 0, DateTimeKind.Utc), GameJson.ParseServerTime(s.serverTime));
        }

        // ---------------------------------------------------------------- 내 역할

        [Test]
        public void 내_역할_해적은_동료_목록이_있다()
        {
            var me = GameJson.FromJson<MyRoleDto>(GameJsonFixtures.MyRoleRaider);

            Assert.AreEqual(11L, me.playerId);
            Assert.AreEqual(RoleCodes.PirateRaider, me.role);
            Assert.AreEqual("해적", me.roleName);
            Assert.AreEqual(Factions.Pirate, me.faction);
            Assert.AreEqual(ActionCodes.SelectAttackTarget, me.actionCode);
            Assert.IsTrue(me.HasAbility);
            Assert.IsTrue(me.IsUnlimited);
            CollectionAssert.AreEqual(new long[] { 13, 14 }, me.mafiaTeammateIds);
            Assert.IsFalse(me.contacted);
        }

        [Test]
        public void 내_역할_선원은_능력이_없다()
        {
            var me = GameJson.FromJson<MyRoleDto>(GameJsonFixtures.MyRoleSailor);

            Assert.IsFalse(me.HasAbility, "actionCode null → 능력 없음");
            Assert.IsFalse(me.alive);
            Assert.IsNotNull(me.mafiaTeammateIds);
            Assert.AreEqual(0, me.mafiaTeammateIds.Count);
        }

        [Test]
        public void 내_역할_주정뱅이는_남은_횟수가_있다()
        {
            var me = GameJson.FromJson<MyRoleDto>(GameJsonFixtures.MyRoleDrunk);

            Assert.AreEqual(ActionCodes.ReadCorpseRole, me.actionCode);
            Assert.AreEqual(2, me.remainingUses);
            Assert.IsFalse(me.IsUnlimited);
        }

        [Test]
        public void 내_역할_접선한_앵무새()
        {
            var me = GameJson.FromJson<MyRoleDto>(GameJsonFixtures.MyRoleParrotContacted);

            Assert.AreEqual(RoleCodes.PirateParrot, me.role);
            Assert.IsTrue(me.contacted);
            CollectionAssert.AreEqual(new long[] { 11, 13 }, me.mafiaTeammateIds);
        }

        // ---------------------------------------------------------------- 밤 능력

        [Test]
        public void 밤_능력_응답_접선하면_해적_id가_온다()
        {
            var r = GameJson.FromJson<NightActionResultDto>(GameJsonFixtures.NightActionContact);

            Assert.IsTrue(r.accepted);
            Assert.AreEqual(GamePhases.Night, r.phase);
            CollectionAssert.AreEqual(new long[] { 11, 13 }, r.contactedPirateIds);
        }

        [Test]
        public void 밤_능력_응답_전원_제출로_판정되면_페이즈가_바뀐다()
        {
            var r = GameJson.FromJson<NightActionResultDto>(GameJsonFixtures.NightActionResolved);

            Assert.AreEqual(GamePhases.NightResult, r.phase);
            Assert.AreEqual(8L, r.phaseVersion);
            Assert.AreEqual(0, r.contactedPirateIds.Count);
        }

        // ---------------------------------------------------------------- 밤 결과

        [Test]
        public void 밤_결과_사망자와_조사_결과()
        {
            var n = GameJson.FromJson<NightResultDto>(GameJsonFixtures.NightResultKilledFaction);

            Assert.IsTrue(n.HasKill);
            Assert.AreEqual(12L, n.killedPlayerId);
            Assert.AreEqual("영희", n.killedNickname);
            Assert.IsFalse(n.protectedByDoctor);
            Assert.AreEqual(1, n.reports.Count);

            var report = n.reports[0];
            Assert.AreEqual(ReportTypes.Faction, report.type);
            Assert.AreEqual(11L, report.targetId);
            Assert.AreEqual("철수", report.targetNickname);
            Assert.AreEqual(Factions.Pirate, report.faction);
            Assert.AreEqual(0, report.players.Count);
            Assert.AreEqual(0, report.actions.Count);
        }

        [Test]
        public void 밤_결과_사망자가_없으면_id가_0이다()
        {
            var n = GameJson.FromJson<NightResultDto>(GameJsonFixtures.NightResultSavedAllTypes);

            Assert.IsFalse(n.HasKill, "killedPlayerId null → 0");
            Assert.AreEqual(0L, n.killedPlayerId);
            Assert.IsTrue(string.IsNullOrEmpty(n.killedNickname));
            Assert.IsTrue(n.protectedByDoctor);
        }

        [Test]
        public void 밤_결과_망루지기_방문자()
        {
            var visitors = GameJson.FromJson<NightResultDto>(GameJsonFixtures.NightResultSavedAllTypes).reports[0];

            Assert.AreEqual(ReportTypes.Visitors, visitors.type);
            Assert.AreEqual(12L, visitors.targetId);
            Assert.AreEqual(2, visitors.players.Count);
            Assert.AreEqual(11L, visitors.players[0].playerId);
            Assert.AreEqual("민수", visitors.players[1].nickname);
            Assert.IsTrue(string.IsNullOrEmpty(visitors.faction));
        }

        [Test]
        public void 밤_결과_앵무새_행동_관찰()
        {
            var actions = GameJson.FromJson<NightResultDto>(GameJsonFixtures.NightResultSavedAllTypes).reports[1];

            Assert.AreEqual(ReportTypes.Actions, actions.type);
            Assert.AreEqual(17L, actions.targetId);
            Assert.AreEqual(1, actions.actions.Count);
            Assert.AreEqual(ActionCodes.Protect, actions.actions[0].actionCode);
            Assert.AreEqual(12L, actions.actions[0].targetId);
            Assert.AreEqual("영희", actions.actions[0].targetNickname);
        }

        [Test]
        public void 밤_결과_주정뱅이_시체_직업()
        {
            var corpse = GameJson.FromJson<NightResultDto>(GameJsonFixtures.NightResultSavedAllTypes).reports[2];

            Assert.AreEqual(ReportTypes.CorpseRole, corpse.type);
            Assert.AreEqual(15L, corpse.targetId);
            Assert.AreEqual(RoleCodes.CrewMonkey, corpse.roleCode);
            Assert.AreEqual("원숭이", corpse.roleName);
        }

        [Test]
        public void 밤_결과_개인_결과가_없으면_빈_목록이다()
        {
            var n = GameJson.FromJson<NightResultDto>(GameJsonFixtures.NightResultEmpty);

            Assert.IsNotNull(n.reports);
            Assert.AreEqual(0, n.reports.Count);
        }

        // ---------------------------------------------------------------- 투표 · 처형

        [Test]
        public void 투표_응답_접수()
        {
            var v = GameJson.FromJson<VoteResultDto>(GameJsonFixtures.VoteAccepted);

            Assert.IsTrue(v.accepted);
            Assert.AreEqual(GamePhases.Vote, v.phase);
            Assert.AreEqual(11L, v.phaseVersion);
        }

        [Test]
        public void 투표_응답_전원_투표로_처형_페이즈가_된다()
        {
            var v = GameJson.FromJson<VoteResultDto>(GameJsonFixtures.VoteResolved);

            Assert.IsTrue(v.accepted);
            Assert.AreEqual(GamePhases.Execution, v.phase);
            Assert.AreEqual(12L, v.phaseVersion);
        }

        [Test]
        public void 처형_결과_득표_목록은_votes로_읽는다()
        {
            var e = GameJson.FromJson<ExecutionResultDto>(GameJsonFixtures.ExecutionExecuted);

            Assert.IsTrue(e.HasExecution);
            Assert.AreEqual(11L, e.executedPlayerId);
            Assert.AreEqual("철수", e.executedNickname);
            Assert.IsFalse(e.tie);
            Assert.AreEqual(2, e.votes.Count);
            Assert.AreEqual(11L, e.votes[0].playerId);
            Assert.AreEqual("철수", e.votes[0].nickname);
            Assert.AreEqual(3, e.votes[0].count);
        }

        [Test]
        public void 처형_결과_동률이면_처형이_없다()
        {
            var e = GameJson.FromJson<ExecutionResultDto>(GameJsonFixtures.ExecutionTie);

            Assert.IsTrue(e.tie);
            Assert.IsFalse(e.HasExecution);
            Assert.AreEqual(2, e.votes.Count);
        }

        [Test]
        public void 처형_결과_투표가_없으면_빈_목록이다()
        {
            var e = GameJson.FromJson<ExecutionResultDto>(GameJsonFixtures.ExecutionNoVotes);

            Assert.IsFalse(e.HasExecution);
            Assert.IsFalse(e.tie);
            Assert.IsNotNull(e.votes);
            Assert.AreEqual(0, e.votes.Count);
        }

        // ---------------------------------------------------------------- 게임 결과 · 직업 목록 · 오류

        [Test]
        public void 게임_결과는_실제_직업을_보여준다()
        {
            var g = GameJson.FromJson<GameResultDto>(GameJsonFixtures.GameResult);

            Assert.IsTrue(g.ended);
            Assert.AreEqual(Factions.Crew, g.winner);
            Assert.AreEqual(3, g.lastDay);
            Assert.AreEqual(2, g.players.Count);
            Assert.AreEqual(RoleCodes.CrewMonkey, g.players[1].role, "원숭이는 결과에서 실제 직업으로 나온다");
            Assert.AreEqual("원숭이", g.players[1].roleName);
        }

        [Test]
        public void 게임_결과_진행_중이면_ended가_false다()
        {
            var g = GameJson.FromJson<GameResultDto>(GameJsonFixtures.GameResultNotEnded);

            Assert.IsFalse(g.ended);
            Assert.AreEqual(0, g.players.Count);
        }

        [Test]
        public void 직업_목록은_활성_직업_9종이다()
        {
            var list = GameJson.FromJson<RoleListDto>(GameJsonFixtures.RoleList);

            Assert.AreEqual(9, list.roles.Count);

            var sailor = list.roles.Single(r => r.code == RoleCodes.CrewSailor);
            Assert.AreEqual("선원", sailor.name);
            Assert.AreEqual(Factions.Crew, sailor.faction);
            Assert.IsTrue(string.IsNullOrEmpty(sailor.actionCode));

            var raider = list.roles.Single(r => r.code == RoleCodes.PirateRaider);
            Assert.AreEqual(Factions.Pirate, raider.faction);
            Assert.AreEqual(ActionCodes.SelectAttackTarget, raider.actionCode);
        }

        [Test]
        public void 직업_목록의_코드는_모두_상수에_있다()
        {
            // 서버에 직업·능력이 추가되거나 이름이 바뀌면 여기서 먼저 깨진다.
            var list = GameJson.FromJson<RoleListDto>(GameJsonFixtures.RoleList);
            HashSet<string> roleCodes = ConstValues(typeof(RoleCodes));
            HashSet<string> actionCodes = ConstValues(typeof(ActionCodes));
            HashSet<string> factions = ConstValues(typeof(Factions));

            CollectionAssert.AreEquivalent(roleCodes, list.roles.Select(r => r.code));
            foreach (RoleDto r in list.roles)
            {
                Assert.IsTrue(factions.Contains(r.faction), r.code + " faction: " + r.faction);
                if (!string.IsNullOrEmpty(r.actionCode))
                {
                    Assert.IsTrue(actionCodes.Contains(r.actionCode), r.code + " actionCode: " + r.actionCode);
                }
            }
        }

        [TestCase(GameJsonFixtures.Error409, GameErrorCodes.GameRuleViolation, "이틀 연속으로 자신을 보호할 수 없습니다.")]
        [TestCase(GameJsonFixtures.Error404, GameErrorCodes.GameNotFound, "게임을 찾을 수 없습니다: no-such-game")]
        public void 오류_응답(string json, string code, string message)
        {
            var err = GameJson.FromJson<GameErrorDto>(json);

            Assert.AreEqual(code, err.code);
            Assert.AreEqual(message, err.message);
        }

        // ---------------------------------------------------------------- 요청 · 도우미

        [Test]
        public void 요청_body는_targetId_하나다()
        {
            Assert.AreEqual("{\"targetId\":5}", GameJson.TargetBody(5));
        }

        [Test]
        public void 본문이_비어_있으면_null이다()
        {
            Assert.IsNull(GameJson.FromJson<GameStateDto>(""));
            Assert.IsNull(GameJson.FromJson<GameStateDto>(null));
        }

        private static HashSet<string> ConstValues(Type type)
        {
            return new HashSet<string>(type.GetFields(BindingFlags.Public | BindingFlags.Static)
                .Where(f => f.IsLiteral)
                .Select(f => (string)f.GetRawConstantValue()));
        }
    }
}
