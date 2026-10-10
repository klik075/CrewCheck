using System.Collections.Generic;
using NUnit.Framework;
using static WhoisntCitizen.Game.Tests.GameTestData;

namespace WhoisntCitizen.Game.Tests
{
    public class NightRoomRulesTests
    {
        /// <summary>11 철수(나), 12 영희, 13 민수, 15 수진 생존 + 14 지훈 사망</summary>
        private static GameStateDto Night()
        {
            return State(GamePhases.Night, 7, P(11, "철수", true), P(12, "영희", true), P(13, "민수", true), P(14, "지훈", false), P(15, "수진", true));
        }

        private static MyRoleDto Pirate(string role, params long[] teammates)
        {
            MyRoleDto me = Me(11, role, ActionCodes.SelectAttackTarget, -1);
            me.faction = Factions.Pirate;
            me.mafiaTeammateIds.AddRange(teammates);
            return me;
        }

        private static List<long> Ids(NightRoom room)
        {
            var ids = new List<long>();
            foreach (PlayerViewDto p in room.Members) ids.Add(p.playerId);
            return ids;
        }

        [Test]
        public void 시민은_자기_직업_방에_혼자_있다()
        {
            MyRoleDto me = Me(11, RoleCodes.CrewDoctor, ActionCodes.Protect, -1);
            me.faction = Factions.Crew;

            NightRoom room = NightRoomRules.RoomFor(me, Night(), null);

            Assert.AreEqual("CREW_DOCTOR", room.DesignId);
            Assert.IsFalse(room.Shared);
            CollectionAssert.AreEqual(new long[] { 11 }, Ids(room));
        }

        [Test]
        public void 해적은_살아_있는_동료와_공용_방에_있다_나_먼저_사망자_제외()
        {
            NightRoom room = NightRoomRules.RoomFor(Pirate(RoleCodes.PirateRaider, 15, 14, 12), Night(), null);

            Assert.AreEqual(NightRoomRules.PirateShared, room.DesignId);
            Assert.IsTrue(room.Shared);
            CollectionAssert.AreEqual(new long[] { 11, 12, 15 }, Ids(room)); // 나, 그다음 state.players 순서. 14 지훈은 사망
        }

        [Test]
        public void 동료가_모두_죽은_해적은_자기_방에_혼자_있다()
        {
            NightRoom room = NightRoomRules.RoomFor(Pirate(RoleCodes.PirateRaider, 14), Night(), null);

            Assert.AreEqual("PIRATE_RAIDER", room.DesignId);
            CollectionAssert.AreEqual(new long[] { 11 }, Ids(room));
        }

        [Test]
        public void 앵무새는_접선_전에는_혼자_접선하면_해적_방에_들어간다()
        {
            MyRoleDto parrot = Pirate(RoleCodes.PirateParrot); // 접선 전: 서버가 동료를 알려 주지 않는다
            parrot.actionCode = ActionCodes.WatchAction;

            NightRoom before = NightRoomRules.RoomFor(parrot, Night(), new List<long>());
            Assert.AreEqual("PIRATE_PARROT", before.DesignId);
            CollectionAssert.AreEqual(new long[] { 11 }, Ids(before));

            // 이번 밤 접선 응답(contactedPirateIds)만 받았고 /me는 아직 옛것
            NightRoom after = NightRoomRules.RoomFor(parrot, Night(), new List<long> { 13 });
            Assert.AreEqual(NightRoomRules.PirateShared, after.DesignId);
            CollectionAssert.AreEqual(new long[] { 11, 13 }, Ids(after));
        }

        [Test]
        public void 해적_진영이_아니면_동료_목록이_있어도_혼자_있다()
        {
            MyRoleDto me = Me(11, RoleCodes.CrewCaptain, ActionCodes.InvestigateFaction, -1);
            me.faction = Factions.Crew;
            me.mafiaTeammateIds.Add(12);

            NightRoom room = NightRoomRules.RoomFor(me, Night(), new List<long> { 13 });

            Assert.AreEqual("CREW_CAPTAIN", room.DesignId);
            CollectionAssert.AreEqual(new long[] { 11 }, Ids(room));
        }

        [Test]
        public void 죽은_나도_방에_남는다()
        {
            GameStateDto state = State(GamePhases.Night, 7, P(11, "철수", false), P(12, "영희", true));
            MyRoleDto me = Me(11, RoleCodes.CrewLookout, ActionCodes.WatchVisitors, -1);
            me.faction = Factions.Crew;

            NightRoom room = NightRoomRules.RoomFor(me, state, null);

            Assert.AreEqual(1, room.Members.Count);
            Assert.IsFalse(room.Members[0].alive);
        }

        [TestCase("NEUTRAL_SIREN", "CONCEPT_SIREN")]
        [TestCase("NEUTRAL_KRAKEN", "PIRATE_KRAKEN")]
        [TestCase("NEUTRAL_GHOST_CAPTAIN", "CONCEPT_GHOST_CAPTAIN")]
        [TestCase("NEUTRAL_MERMAID", "THIRD_MERMAID")]
        [TestCase("PIRATE_COOK", "PIRATE_COOK")]
        [TestCase("CREW_BOATSWAIN", "CREW_BOATSWAIN")]
        [TestCase(null, null)]
        public void 직업_코드를_아트_디자인_코드로_바꾼다(string role, string design)
        {
            Assert.AreEqual(design, NightRoomRules.DesignIdFor(role));
        }
    }
}
