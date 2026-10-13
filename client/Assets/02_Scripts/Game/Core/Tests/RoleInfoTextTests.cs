using System.Collections.Generic;
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
