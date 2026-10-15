using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace WhoisntCitizen.Game.Tests
{
    public class RoleInfoTextTests
    {
        private static RoleSetupOptionsDto Options()
        {
            var options = new RoleSetupOptionsDto { minPlayers = 4, maxPlayers = 12 };
            options.roles.Add(new RoleDto { code = RoleCodes.PirateRaider, name = "해적", faction = Factions.Pirate });
            options.roles.Add(new RoleDto { code = RoleCodes.PirateParrot, name = "앵무새", faction = Factions.Pirate });
            options.roles.Add(new RoleDto { code = RoleCodes.CrewCaptain, name = "선장", faction = Factions.Crew });
            options.roles.Add(new RoleDto { code = RoleCodes.CrewDoctor, name = "선의", faction = Factions.Crew });
            options.roles.Add(new RoleDto { code = RoleCodes.CrewSailor, name = "선원", faction = Factions.Crew });
            var six = new RoleCompositionDto { playerCount = 6 };
            six.roles.AddRange(new[] { RoleCodes.CrewSailor, RoleCodes.PirateRaider, RoleCodes.CrewDoctor, RoleCodes.CrewSailor, RoleCodes.CrewCaptain, RoleCodes.PirateRaider });
            options.recommended.Add(six);
            return options;
        }

        [Test]
        public void 구성은_직업_목록_순서대로_인원과_함께()
        {
            List<RoleCount> lineup = RoleInfoText.Lineup(Options(), 6);

            Assert.AreEqual(4, lineup.Count);
            Assert.AreEqual("해적", lineup[0].Role.name); Assert.AreEqual(2, lineup[0].Count);
            Assert.AreEqual("선장", lineup[1].Role.name); Assert.AreEqual(1, lineup[1].Count);
            Assert.AreEqual("선의", lineup[2].Role.name); Assert.AreEqual(1, lineup[2].Count);
            Assert.AreEqual("선원", lineup[3].Role.name); Assert.AreEqual(2, lineup[3].Count);
        }

        [Test]
        public void 추천_구성이_없는_인원이면_빈_목록()
        {
            Assert.AreEqual(0, RoleInfoText.Lineup(Options(), 9).Count);
            Assert.AreEqual(0, RoleInfoText.Lineup(null, 6).Count);
        }

        [Test]
        public void 목록에_없는_직업도_코드로_넣는다()
        {
            RoleSetupOptionsDto options = Options();
            options.recommended[0].roles[0] = "PIRATE_COOK";

            List<RoleCount> lineup = RoleInfoText.Lineup(options, 6);

            Assert.AreEqual("PIRATE_COOK", lineup[lineup.Count - 1].Role.code);
            Assert.AreEqual(1, lineup[lineup.Count - 1].Count);
        }

        [Test]
        public void 커스텀_구성이면_넣어_둔_중립_직업도_나온다()
        {
            RoleSetupOptionsDto options = Options();
            options.roles.Add(new RoleDto { code = "NEUTRAL_KRAKEN", name = "크라켄", faction = Factions.Neutral });
            var setup = new RoleSetupDto { mode = RoleSetupModes.Custom };
            setup.customCompositions.Add(new RoleCompositionDto
            {
                playerCount = 6,
                roles = new List<string> { RoleCodes.PirateRaider, RoleCodes.CrewCaptain, RoleCodes.CrewDoctor, "NEUTRAL_KRAKEN", RoleCodes.CrewSailor, RoleCodes.CrewSailor }
            });

            List<RoleCount> lineup = RoleInfoText.Lineup(options, setup, 6);

            CollectionAssert.AreEqual(new[] { "해적", "선장", "선의", "선원", "크라켄" }, lineup.Select(l => l.Role.name).ToArray());
            RoleCount kraken = lineup.Single(l => l.Role.code == "NEUTRAL_KRAKEN");
            Assert.AreEqual(1, kraken.Count);
            Assert.AreEqual(Factions.Neutral, kraken.Role.faction);
            Assert.AreEqual(0, RoleInfoText.Lineup(options, setup, 7).Count, "편집하지 않은 인원수는 추천 구성 (7인 추천이 없어 빈 목록)");
        }

        [Test]
        public void 랜덤_구성이면_나올_수_있는_직업을_인원_없이_넣는다()
        {
            RoleSetupOptionsDto options = Options();
            var four = new RoleCompositionDto { playerCount = 4 };
            four.roles.AddRange(new[] { RoleCodes.PirateRaider, RoleCodes.CrewCaptain, RoleCodes.CrewDoctor, RoleCodes.CrewSailor });
            options.recommended.Add(four);
            var setup = new RoleSetupDto { mode = RoleSetupModes.Random };
            setup.randomCandidates.AddRange(new[] { RoleCodes.PirateParrot, RoleCodes.CrewCaptain });

            // 6인: 해적 진영 2자리라 앵무새도 나올 수 있고, 선원 진영 4자리에 후보가 선장 하나뿐이라 남는 자리는 선원
            List<RoleCount> six = RoleInfoText.Lineup(options, setup, 6);
            CollectionAssert.AreEqual(new[] { "해적", "앵무새", "선장", "선원" }, six.Select(l => l.Role.name).ToArray());
            Assert.IsTrue(six.All(l => l.Count == 0));

            // 4인: 해적 진영이 1자리(해적)뿐이라 앵무새는 나오지 않는다
            List<RoleCount> fourLineup = RoleInfoText.Lineup(options, setup, 4);
            CollectionAssert.AreEqual(new[] { "해적", "선장", "선원" }, fourLineup.Select(l => l.Role.name).ToArray());
        }

        [Test]
        public void 부제는_이번_판의_배정_방식()
        {
            var custom = new RoleSetupDto { mode = RoleSetupModes.Custom };
            custom.customCompositions.Add(new RoleCompositionDto { playerCount = 6 });

            Assert.AreEqual("6명 게임 · 커스텀 구성", RoleInfoText.LineupSubtitle(custom, 6));
            Assert.AreEqual("7명 게임 · 추천 구성", RoleInfoText.LineupSubtitle(custom, 7)); // 편집하지 않은 인원수
            Assert.AreEqual("6명 게임 · 추천 구성", RoleInfoText.LineupSubtitle(null, 6));  // 방 설정을 모름
            StringAssert.StartsWith("6명 게임 · 랜덤 구성", RoleInfoText.LineupSubtitle(new RoleSetupDto { mode = RoleSetupModes.Random }, 6));
        }

        [Test]
        public void 진영_이름과_목표()
        {
            Assert.AreEqual("선원 진영", RoleInfoText.FactionName(Factions.Crew));
            Assert.AreEqual("해적 진영", RoleInfoText.FactionName(Factions.Pirate));
            Assert.AreEqual("중립", RoleInfoText.FactionName(Factions.Neutral));
            Assert.AreEqual("해적을 찾아내세요", RoleInfoText.Goal(RoleCodes.CrewDoctor, Factions.Crew));
            Assert.AreEqual("선원을 제압하세요", RoleInfoText.Goal(RoleCodes.PirateParrot, Factions.Pirate));
            Assert.AreEqual("투표로 처형되세요", RoleInfoText.Goal("NEUTRAL_MERMAID", Factions.Neutral));
        }

        [Test]
        public void 유혹당하면_세이렌_팀의_진영과_목표를_보여_준다()
        {
            var seduced = new MyRoleDto { playerId = 11, role = RoleCodes.CrewDoctor, faction = Factions.Crew, team = Teams.Siren };
            var crew = new MyRoleDto { playerId = 12, role = RoleCodes.CrewDoctor, faction = Factions.Crew, team = Teams.Crew };

            Assert.AreEqual("세이렌 팀", RoleInfoText.TeamName(seduced));
            Assert.AreEqual(RoleInfoText.Goal("NEUTRAL_SIREN", Factions.Neutral), RoleInfoText.Goal(seduced));
            Assert.AreEqual("선원 진영", RoleInfoText.TeamName(crew));
            Assert.AreEqual("해적을 찾아내세요", RoleInfoText.Goal(crew));
        }

        [Test]
        public void 모든_서버_직업에_설명이_있다()
        {
            string[] codes =
            {
                RoleCodes.PirateRaider, RoleCodes.PirateParrot, "PIRATE_COOK", RoleCodes.CrewCaptain, RoleCodes.CrewDoctor,
                RoleCodes.CrewLookout, RoleCodes.CrewBoatswain, RoleCodes.CrewDrunk, RoleCodes.CrewSailor, RoleCodes.CrewMonkey,
                "NEUTRAL_SIREN", "NEUTRAL_KRAKEN", "NEUTRAL_GHOST_CAPTAIN", "NEUTRAL_MERMAID"
            };
            foreach (string code in codes)
            {
                Assert.AreNotEqual(RoleInfoText.Description(null), RoleInfoText.Description(code), code);
            }
        }
    }
}
