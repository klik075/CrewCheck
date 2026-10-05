using System.Collections.Generic;
using System.Linq;

namespace WhoisntCitizen.Game
{
    /// <summary>
    /// 직업 설정 화면의 계산 (Room 씬 RoleSetupPanel이 사용). 화면 안내와 편집용이고 최종 검증은 서버가 한다.
    /// 구성 규칙은 서버 RoleAssigner와 같다: 직업 수 = 인원수, 공격할 해적 1명 이상, 해적 진영 &lt; 선원 진영.
    ///
    /// 커스텀 편집 방식
    ///   선원(CREW_SAILOR)이 남는 자리를 채운다. 다른 직업을 +하면 선원 한 자리가 그 직업이 되고,
    ///   -하면 그 자리가 선원으로 돌아간다. 그래서 편집 중에도 직업 수는 항상 인원수와 같다.
    /// </summary>
    public static class RoleSetupRules
    {
        // ---------------------------------------------------------------- 직업 정보

        /// <summary>해적·선원: 진영 자리를 채우는 기본 직업. 랜덤에서 항상 후보이고 중복될 수 있다.</summary>
        public static bool IsBaseRole(string code)
        {
            return code == RoleCodes.PirateRaider || code == RoleCodes.CrewSailor;
        }

        public static RoleDto FindRole(RoleSetupOptionsDto options, string code)
        {
            return options?.roles?.FirstOrDefault(r => r != null && r.code == code);
        }

        /// <summary>직업 이름. 선택지에 없으면 코드를 그대로 보여 준다.</summary>
        public static string RoleName(RoleSetupOptionsDto options, string code)
        {
            RoleDto role = FindRole(options, code);
            return role != null && !string.IsNullOrEmpty(role.name) ? role.name : code;
        }

        public static bool IsPirate(RoleSetupOptionsDto options, string code)
        {
            RoleDto role = FindRole(options, code);
            if (role != null) return role.faction == Factions.Pirate;
            return code != null && code.StartsWith("PIRATE_");
        }

        // ---------------------------------------------------------------- 구성 조회

        /// <summary>인원수별 추천 구성 (복사본). 없는 인원수면 빈 목록.</summary>
        public static List<string> Recommended(RoleSetupOptionsDto options, int playerCount)
        {
            RoleCompositionDto found = options?.recommended?.FirstOrDefault(c => c != null && c.playerCount == playerCount);
            return found?.roles != null ? new List<string>(found.roles) : new List<string>();
        }

        /// <summary>커스텀 표에서 해당 인원수의 구성. 편집하지 않은 인원수면 null.</summary>
        public static RoleCompositionDto FindCustom(RoleSetupDto setup, int playerCount)
        {
            return setup?.customCompositions?.FirstOrDefault(c => c != null && c.playerCount == playerCount);
        }

        /// <summary>
        /// 이 인원수로 시작하면 나올 구성 (복사본). 추천은 추천 구성, 커스텀은 편집한 구성(없으면 추천 구성).
        /// 랜덤은 정해진 구성이 없어서 null.
        /// </summary>
        public static List<string> FixedComposition(RoleSetupDto setup, RoleSetupOptionsDto options, int playerCount)
        {
            string mode = RoleSetupModes.Normalize(setup?.mode);
            if (mode == RoleSetupModes.Random) return null;
            if (mode == RoleSetupModes.Custom)
            {
                RoleCompositionDto custom = FindCustom(setup, playerCount);
                if (custom?.roles != null) return new List<string>(custom.roles);
            }
            return Recommended(options, playerCount);
        }

        /// <summary>해적 진영 자리 수. 랜덤도 추천 구성과 같은 수를 쓴다.</summary>
        public static int PirateSlots(RoleSetupOptionsDto options, int playerCount)
        {
            return CountPirates(options, Recommended(options, playerCount));
        }

        public static int Count(List<string> roles, string code)
        {
            return roles == null ? 0 : roles.Count(r => r == code);
        }

        public static int CountPirates(RoleSetupOptionsDto options, List<string> roles)
        {
            return roles == null ? 0 : roles.Count(r => IsPirate(options, r));
        }

        // ---------------------------------------------------------------- 커스텀 편집 (선원이 남는 자리를 채움)

        /// <summary>+ 가능: 선원이 아니고, 바꿀 선원 자리가 남아 있음</summary>
        public static bool CanAdd(List<string> roles, string code)
        {
            return code != RoleCodes.CrewSailor && Count(roles, RoleCodes.CrewSailor) > 0;
        }

        /// <summary>- 가능: 선원이 아니고, 그 직업이 1명 이상 있음</summary>
        public static bool CanRemove(List<string> roles, string code)
        {
            return code != RoleCodes.CrewSailor && Count(roles, code) > 0;
        }

        /// <summary>선원 한 자리를 code로 바꾼 새 목록 (표시 순서로 정렬). 바꿀 수 없으면 그대로.</summary>
        public static List<string> Add(RoleSetupOptionsDto options, List<string> roles, string code)
        {
            var result = new List<string>(roles ?? new List<string>());
            if (!CanAdd(result, code)) return Sorted(options, result);
            result.Remove(RoleCodes.CrewSailor);
            result.Add(code);
            return Sorted(options, result);
        }

        /// <summary>code 한 자리를 선원으로 바꾼 새 목록 (표시 순서로 정렬). 바꿀 수 없으면 그대로.</summary>
        public static List<string> Remove(RoleSetupOptionsDto options, List<string> roles, string code)
        {
            var result = new List<string>(roles ?? new List<string>());
            if (!CanRemove(result, code)) return Sorted(options, result);
            result.Remove(code);
            result.Add(RoleCodes.CrewSailor);
            return Sorted(options, result);
        }

        /// <summary>선택지(options.roles) 순서로 정렬한 새 목록. 선택지에 없는 코드는 뒤에 둔다.</summary>
        public static List<string> Sorted(RoleSetupOptionsDto options, List<string> roles)
        {
            List<string> order = options?.roles?.Where(r => r != null).Select(r => r.code).ToList() ?? new List<string>();
            return (roles ?? new List<string>())
                .OrderBy(code => order.IndexOf(code) < 0 ? int.MaxValue : order.IndexOf(code))
                .ThenBy(code => code)
                .ToList();
        }

        /// <summary>커스텀 표에 해당 인원수의 구성을 넣는다 (있으면 바꿈). 표는 인원수 순으로 유지한다.</summary>
        public static void SetCustom(RoleSetupDto setup, int playerCount, List<string> roles)
        {
            RemoveCustom(setup, playerCount);
            setup.customCompositions.Add(new RoleCompositionDto { playerCount = playerCount, roles = new List<string>(roles) });
            setup.customCompositions.Sort((a, b) => a.playerCount.CompareTo(b.playerCount));
        }

        /// <summary>커스텀 표에서 해당 인원수를 지운다. 그 인원수는 추천 구성으로 돌아간다.</summary>
        public static void RemoveCustom(RoleSetupDto setup, int playerCount)
        {
            if (setup.customCompositions == null) setup.customCompositions = new List<RoleCompositionDto>();
            setup.customCompositions.RemoveAll(c => c == null || c.playerCount == playerCount);
        }

        // ---------------------------------------------------------------- 검증 (서버 RoleAssigner.problemOf와 같은 문장)

        /// <summary>구성 규칙에 맞지 않으면 이유, 맞으면 null.</summary>
        public static string Problem(RoleSetupOptionsDto options, int playerCount, List<string> roles)
        {
            roles = roles ?? new List<string>();
            string unknown = roles.FirstOrDefault(code => FindRole(options, code) == null);
            if (unknown != null) return $"없는 직업입니다: {unknown}";
            if (roles.Count != playerCount) return $"직업 수({roles.Count})가 인원수({playerCount})와 다릅니다.";
            if (Count(roles, RoleCodes.PirateRaider) == 0) return "공격할 수 있는 해적이 1명 이상 있어야 합니다.";
            int pirates = CountPirates(options, roles);
            if (pirates * 2 >= playerCount)
                return $"해적 진영({pirates}명)은 선원 진영({playerCount - pirates}명)보다 적어야 합니다.";
            return null;
        }

        /// <summary>커스텀 표 전체에서 첫 번째 문제 ("7인 구성: ..."). 문제가 없으면 null.</summary>
        public static string FirstCustomProblem(RoleSetupDto setup, RoleSetupOptionsDto options)
        {
            if (setup?.customCompositions == null) return null;
            foreach (RoleCompositionDto c in setup.customCompositions.Where(c => c != null).OrderBy(c => c.playerCount))
            {
                string problem = Problem(options, c.playerCount, c.roles);
                if (problem != null) return $"{c.playerCount}인 구성: {problem}";
            }
            return null;
        }

        // ---------------------------------------------------------------- 랜덤 후보

        /// <summary>랜덤 후보인지. 해적·선원은 항상 후보다.</summary>
        public static bool IsCandidate(RoleSetupDto setup, string code)
        {
            return IsBaseRole(code) || (setup?.randomCandidates != null && setup.randomCandidates.Contains(code));
        }

        /// <summary>랜덤 후보에 넣거나 뺀다. 후보 목록은 선택지(options.randomCandidates) 순서로 유지한다.</summary>
        public static void SetCandidate(RoleSetupDto setup, RoleSetupOptionsDto options, string code, bool include)
        {
            if (IsBaseRole(code)) return;
            var picked = new HashSet<string>(setup.randomCandidates ?? new List<string>());
            if (include) picked.Add(code);
            else picked.Remove(code);
            setup.randomCandidates = (options?.randomCandidates ?? new List<string>()).Where(picked.Contains).ToList();
        }

        // ---------------------------------------------------------------- 비교

        /// <summary>순서를 빼고 같은 구성인지 (직업별 개수가 같음)</summary>
        public static bool SameRoles(List<string> a, List<string> b)
        {
            return (a ?? new List<string>()).OrderBy(r => r).SequenceEqual((b ?? new List<string>()).OrderBy(r => r));
        }

        /// <summary>서버에 저장된 설정과 같은지 (저장 버튼을 켤지). 구성 안의 순서는 따지지 않는다.</summary>
        public static bool SameSetup(RoleSetupDto a, RoleSetupDto b)
        {
            if (a == null || b == null) return a == b;
            if (RoleSetupModes.Normalize(a.mode) != RoleSetupModes.Normalize(b.mode)) return false;

            var aCandidates = new HashSet<string>(a.randomCandidates ?? new List<string>());
            if (!aCandidates.SetEquals(b.randomCandidates ?? new List<string>())) return false;

            List<RoleCompositionDto> aTable = (a.customCompositions ?? new List<RoleCompositionDto>()).Where(c => c != null).ToList();
            List<RoleCompositionDto> bTable = (b.customCompositions ?? new List<RoleCompositionDto>()).Where(c => c != null).ToList();
            if (aTable.Count != bTable.Count) return false;
            foreach (RoleCompositionDto c in aTable)
            {
                RoleCompositionDto other = bTable.FirstOrDefault(x => x.playerCount == c.playerCount);
                if (other == null || !SameRoles(c.roles, other.roles)) return false;
            }
            return true;
        }

        // ---------------------------------------------------------------- 화면 문장

        /// <summary>"해적 2, 선장 1, 선의 1, 선원 3" (선택지 순서)</summary>
        public static string DescribeComposition(RoleSetupOptionsDto options, List<string> roles)
        {
            if (roles == null || roles.Count == 0) return "";
            return string.Join(", ", Sorted(options, roles)
                .GroupBy(code => code)
                .Select(g => $"{RoleName(options, g.Key)} {g.Count()}"));
        }

        /// <summary>"해적 진영 2명 / 선원 진영 5명"</summary>
        public static string DescribeFactions(RoleSetupOptionsDto options, List<string> roles)
        {
            int pirates = CountPirates(options, roles);
            int total = roles?.Count ?? 0;
            return $"해적 진영 {pirates}명 / 선원 진영 {total - pirates}명";
        }

        /// <summary>
        /// 랜덤으로 이 인원수일 때 어떻게 뽑히는지 두 줄로 설명한다.
        ///   해적 진영 2명: 해적 1 + 1자리는 해적/앵무새 중 무작위
        ///   선원 진영 5명: 후보 6개 중 5개 무작위
        /// </summary>
        public static string DescribeRandom(RoleSetupDto setup, RoleSetupOptionsDto options, int playerCount)
        {
            int pirates = PirateSlots(options, playerCount);
            int crew = playerCount - pirates;
            List<string> candidates = (setup?.randomCandidates ?? new List<string>()).Where(c => !IsBaseRole(c)).ToList();
            List<string> pirateCandidates = candidates.Where(c => IsPirate(options, c)).ToList();
            int crewCandidates = candidates.Count - pirateCandidates.Count;
            string raider = RoleName(options, RoleCodes.PirateRaider);
            string sailor = RoleName(options, RoleCodes.CrewSailor);

            string piratePart = pirates <= 1 || pirateCandidates.Count == 0
                ? $"해적 진영 {pirates}명: {raider} {pirates}"
                : $"해적 진영 {pirates}명: {raider} 1 + {pirates - 1}자리는 "
                  + string.Join("/", new[] { raider }.Concat(pirateCandidates.Select(c => RoleName(options, c))))
                  + " 중 무작위";

            string crewPart;
            if (crewCandidates >= crew) crewPart = $"선원 진영 {crew}명: 후보 {crewCandidates}개 중 {crew}개 무작위";
            else if (crewCandidates == 0) crewPart = $"선원 진영 {crew}명: {sailor} {crew}";
            else crewPart = $"선원 진영 {crew}명: 후보 {crewCandidates}개 전부 + {sailor} {crew - crewCandidates}";

            return piratePart + "\n" + crewPart;
        }
    }
}
