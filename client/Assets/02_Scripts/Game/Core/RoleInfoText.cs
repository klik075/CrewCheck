using System.Collections.Generic;
using System.Linq;

namespace WhoisntCitizen.Game
{
    /// <summary>직업 구성의 한 줄: 직업과 그 직업의 인원. 랜덤 구성처럼 인원이 아직 정해지지 않았으면 0.</summary>
    public sealed class RoleCount
    {
        public RoleDto Role;
        public int Count;
    }

    /// <summary>
    /// 직업·진영 설명 문구 (하단 내 역할 패널, 상단 지도 버튼의 이번 판 직업). 서버는 이름·진영·능력 코드만 주므로
    /// 설명은 여기서 쓴다. 규칙이 바뀌면(서버 ActionCode, WinConditionChecker) 같이 고친다.
    /// </summary>
    public static class RoleInfoText
    {
        public static string FactionName(string faction)
        {
            switch (faction)
            {
                case Factions.Crew: return "선원 진영";
                case Factions.Pirate: return "해적 진영";
                case Factions.Neutral: return "중립";
                default: return "알 수 없는 진영";
            }
        }

        /// <summary>직업 설명 (두 줄 안쪽).</summary>
        public static string Description(string roleCode)
        {
            switch (roleCode)
            {
                case RoleCodes.PirateRaider: return "밤마다 한 명을 골라 공격합니다. 해적끼리는 서로를 압니다.";
                case RoleCodes.PirateParrot: return "밤에 한 명의 행동을 엿봅니다. 해적을 엿보면 접선해 해적과 합류합니다.";
                case "PIRATE_COOK": return "밤마다 한 명을 골라 다음 투표를 못 하게 합니다. 같은 사람은 연달아 고를 수 없습니다.";
                case RoleCodes.CrewCaptain: return "밤에 한 명을 조사해 어느 진영인지 알아냅니다.";
                case RoleCodes.CrewDoctor: return "밤에 한 명을 공격에서 지킵니다. 자신은 이틀 연속 지킬 수 없습니다.";
                case RoleCodes.CrewLookout: return "밤에 한 명을 지켜보고, 그 사람을 찾아온 사람을 알아냅니다.";
                case RoleCodes.CrewBoatswain: return "밤에 한 명을 묶어 그날 밤 능력을 쓰지 못하게 합니다.";
                case RoleCodes.CrewDrunk: return "죽은 사람 한 명의 직업을 알아냅니다. 한 판에 두 번 쓸 수 있습니다.";
                case RoleCodes.CrewSailor: return "특별한 능력은 없지만, 토론과 추리로 해적을 찾아냅니다.";
                case RoleCodes.CrewMonkey: return "자신이 다른 직업이라고 믿는 선원입니다. 능력을 써도 가짜 결과를 받습니다.";
                case "NEUTRAL_SIREN": return "밤에 한 명을 유혹해 세이렌 팀으로 만듭니다. 성공한 다음 밤은 쉽니다.";
                case "NEUTRAL_KRAKEN": return "밤마다 표식을 남기거나, 표식된 사람을 한꺼번에 처치합니다.";
                case "NEUTRAL_GHOST_CAPTAIN": return "능력은 없지만 밤에 죽지 않습니다.";
                case "NEUTRAL_MERMAID": return "능력은 없습니다. 투표로 처형되면 혼자 승리합니다.";
                default: return "직업 설명이 아직 없습니다.";
            }
        }

        /// <summary>내 역할 패널의 진영 줄. 세이렌 팀(세이렌, 유혹당한 사람)이면 직업의 진영 대신 "세이렌 팀".</summary>
        public static string TeamName(MyRoleDto me)
        {
            return me.IsSirenTeam ? "세이렌 팀" : FactionName(me.faction);
        }

        /// <summary>내 역할 패널의 한 줄 목표. 유혹당했으면 직업과 상관없이 세이렌 팀의 목표.</summary>
        public static string Goal(MyRoleDto me)
        {
            return me.IsSirenTeam ? Goal("NEUTRAL_SIREN", Factions.Neutral) : Goal(me.role, me.faction);
        }

        /// <summary>내 역할 패널의 한 줄 목표. 중립은 직업마다, 나머지는 진영마다 다르다.</summary>
        public static string Goal(string roleCode, string faction)
        {
            switch (roleCode)
            {
                case "NEUTRAL_SIREN": return "세이렌 팀으로 바다를 차지하세요";
                case "NEUTRAL_KRAKEN": return "해적을 뺀 생존자를 2명 이하로";
                case "NEUTRAL_GHOST_CAPTAIN": return "살아남아 죽은 자를 더 많게";
                case "NEUTRAL_MERMAID": return "투표로 처형되세요";
            }
            switch (faction)
            {
                case Factions.Pirate: return "선원을 제압하세요";
                case Factions.Crew: return "해적을 찾아내세요";
                default: return "살아남으세요";
            }
        }

        /// <summary>이 인원의 추천 구성. Lineup(options, null, playerCount)와 같다.</summary>
        public static List<RoleCount> Lineup(RoleSetupOptionsDto options, int playerCount)
        {
            return Lineup(options, null, playerCount);
        }

        /// <summary>
        /// 이번 판의 직업 구성 (지도 버튼): 방의 직업 배정 설정(setup)대로. 추천은 추천 구성, 커스텀은 편집한 구성(넣어 둔 중립 직업 포함),
        /// 랜덤은 인원이 게임 시작 때 정해지므로 나올 수 있는 직업을 인원 0으로 넣는다. setup이 null(방 설정을 모름)이면 추천 구성.
        /// 서버가 보내 준 직업 순서(해적 진영 → 선원 진영 …)대로 넣고, 직업 목록에 없는 코드는 이름 대신 코드로 넣는다. 구성이 없으면 빈 목록.
        /// </summary>
        public static List<RoleCount> Lineup(RoleSetupOptionsDto options, RoleSetupDto setup, int playerCount)
        {
            var lineup = new List<RoleCount>();
            List<string> roles = RoleSetupRules.FixedComposition(setup, options, playerCount);
            bool random = roles == null;
            if (random) roles = RandomPool(setup, options, playerCount);
            var counted = new HashSet<string>();
            if (options != null && options.roles != null)
            {
                foreach (RoleDto role in options.roles)
                {
                    if (role != null && roles.Contains(role.code) && counted.Add(role.code))
                    {
                        lineup.Add(new RoleCount { Role = role, Count = random ? 0 : RoleSetupRules.Count(roles, role.code) });
                    }
                }
            }
            foreach (string code in roles)
            {
                if (counted.Add(code))
                {
                    lineup.Add(new RoleCount { Role = new RoleDto { code = code, name = code }, Count = random ? 0 : RoleSetupRules.Count(roles, code) });
                }
            }
            return lineup;
        }

        /// <summary>지도 버튼의 부제. 예: "7명 게임 · 커스텀 구성". 커스텀이어도 이 인원수를 편집하지 않았으면 추천 구성이다.</summary>
        public static string LineupSubtitle(RoleSetupDto setup, int playerCount)
        {
            string mode = RoleSetupModes.Normalize(setup?.mode);
            if (mode == RoleSetupModes.Custom && RoleSetupRules.FindCustom(setup, playerCount) == null) mode = RoleSetupModes.Recommended;
            string subtitle = playerCount + "명 게임 · " + RoleSetupModes.Label(mode);
            return mode == RoleSetupModes.Random ? subtitle + " (아래 직업 중에서 무작위)" : subtitle;
        }

        /// <summary>
        /// 랜덤 구성에서 나올 수 있는 직업 (서버 RoleAssigner.randomComposition과 같은 규칙). 해적은 항상, 고른 후보 중 해적 진영은
        /// 해적 진영 자리가 2개 이상일 때만, 나머지 후보는 항상, 선원은 나머지 후보가 남은 자리보다 적어 빈자리를 채울 때만.
        /// </summary>
        private static List<string> RandomPool(RoleSetupDto setup, RoleSetupOptionsDto options, int playerCount)
        {
            int pirateSlots = RoleSetupRules.PirateSlots(options, playerCount);
            List<string> candidates = (setup?.randomCandidates ?? new List<string>()).Where(c => !RoleSetupRules.IsBaseRole(c)).ToList();
            var pool = new List<string> { RoleCodes.PirateRaider };
            pool.AddRange(candidates.Where(c => pirateSlots > 1 || !RoleSetupRules.IsPirate(options, c)));
            if (candidates.Count(c => !RoleSetupRules.IsPirate(options, c)) < playerCount - pirateSlots) pool.Add(RoleCodes.CrewSailor);
            return pool;
        }
    }
}
