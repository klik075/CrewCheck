using System.Collections.Generic;

namespace WhoisntCitizen.Game
{
    /// <summary>
    /// 서버 결과를 화면 문구로 바꾼다. 닉네임 뒤에는 항상 "님"을 붙여서 조사(이/가, 을/를)가 받침에 따라 바뀌지 않게 한다.
    /// 원숭이의 가짜 결과도 진짜와 같은 형식으로 온다. 클라이언트는 구분하지 않는다.
    /// </summary>
    public static class ReportFormatter
    {
        // ---------------------------------------------------------------- 이름

        /// <summary>진영 이름. 일반 선원(CREW_SAILOR)의 직업 이름과 헷갈리지 않게 "진영"을 붙인다.</summary>
        public static string FactionName(string faction)
        {
            switch (faction)
            {
                case Factions.Crew: return "선원 진영";
                case Factions.Pirate: return "해적 진영";
                default: return faction ?? string.Empty;
            }
        }

        /// <summary>능력 버튼 이름. 모르는 코드면 "능력".</summary>
        public static string ActionName(string actionCode)
        {
            switch (actionCode)
            {
                case ActionCodes.SelectAttackTarget: return "공격";
                case ActionCodes.InvestigateFaction: return "조사";
                case ActionCodes.Protect: return "보호";
                case ActionCodes.WatchVisitors: return "감시";
                case ActionCodes.Block: return "차단";
                case ActionCodes.ReadCorpseRole: return "시체 확인";
                case ActionCodes.WatchAction: return "관찰";
                default: return "능력";
            }
        }

        /// <summary>밤 능력 버튼을 끈 이유. None이면 빈 문자열.</summary>
        public static string AbilityBlockMessage(AbilityBlock block)
        {
            switch (block)
            {
                case AbilityBlock.None: return string.Empty;
                case AbilityBlock.NotNight: return "밤에만 능력을 쓸 수 있습니다.";
                case AbilityBlock.Dead: return "사망해서 능력을 쓸 수 없습니다.";
                case AbilityBlock.LockedByContact: return "해적과 접선해서 오늘 밤 행동이 확정되었습니다.";
                case AbilityBlock.NoAbility: return "밤에 쓰는 능력이 없습니다.";
                case AbilityBlock.UnknownAction: return "지원하지 않는 능력입니다. 게임을 업데이트해 주세요.";
                case AbilityBlock.NoUsesLeft: return "능력을 모두 사용했습니다.";
                case AbilityBlock.NoCorpse: return "아직 사망자가 없어 능력을 쓸 수 없습니다.";
                default: return string.Empty;
            }
        }

        // ---------------------------------------------------------------- 밤

        /// <summary>밤 결과 공개 문구 (전원에게 같은 내용).</summary>
        public static string NightSummary(NightResultDto result)
        {
            if (result.HasKill)
            {
                return Name(result.killedNickname, result.killedPlayerId) + "님이 밤사이 사망했습니다.";
            }
            if (result.protectedByDoctor)
            {
                return "선의의 보호로 아무도 죽지 않았습니다.";
            }
            return "밤사이 아무도 죽지 않았습니다.";
        }

        /// <summary>내 개인 결과 문구 목록. 개인 결과가 없으면 빈 목록.</summary>
        public static List<string> NightReports(NightResultDto result)
        {
            var lines = new List<string>();
            foreach (ReportDto report in result.reports)
            {
                string line = Report(report);
                if (!string.IsNullOrEmpty(line))
                {
                    lines.Add(line);
                }
            }
            return lines;
        }

        /// <summary>갑판장에게 차단당한 사람에게 보여 주는 문구 (본인에게만)</summary>
        public const string BlockedMessage = "갑판장에 의해 차단되어 이번 밤 능력을 사용할 수 없었습니다.";

        /// <summary>
        /// 개인 결과 한 건의 문구. 모르는 종류면 빈 문자열. 능력이 실제로 적용됐을 때만 서버가 보내므로 "~했습니다." + 결과로 쓴다.
        /// ACTIONS에서 행동이 여러 개면 줄바꿈으로 잇는다.
        /// </summary>
        public static string Report(ReportDto report)
        {
            string target = Name(report.targetNickname, report.targetId) + "님";
            switch (report.type)
            {
                case ReportTypes.Faction: // 선장
                    return target + "을 조사했습니다. " + target + "은 " + FactionName(report.faction) + "입니다.";

                case ReportTypes.CorpseRole: // 주정뱅이
                    string role = string.IsNullOrEmpty(report.roleName) ? report.roleCode : report.roleName;
                    return target + "의 시체를 확인했습니다. " + target + "의 직업은 " + role + "입니다.";

                case ReportTypes.Visitors: // 망루지기
                    if (report.players.Count == 0)
                    {
                        return target + "을 감시했습니다. " + target + "을 찾아온 사람이 없습니다.";
                    }
                    var visitors = new List<string>();
                    foreach (PlayerRefDto v in report.players)
                    {
                        visitors.Add(Name(v.nickname, v.playerId) + "님");
                    }
                    return target + "을 감시했습니다. " + target + "을 찾아온 사람: " + string.Join(", ", visitors.ToArray());

                case ReportTypes.Actions: // 앵무새
                    if (report.actions.Count == 0)
                    {
                        return target + "을 관찰했습니다. " + target + "은 아무 행동도 하지 않았습니다.";
                    }
                    var actions = new List<string>();
                    actions.Add(target + "을 관찰했습니다.");
                    foreach (ActionViewDto a in report.actions)
                    {
                        actions.Add(ActionSentence(target, a));
                    }
                    return string.Join("\n", actions.ToArray());

                case ReportTypes.Block: // 갑판장
                    return target + "을 차단했습니다. " + target + "은 이번 밤 능력을 사용할 수 없습니다.";

                case ReportTypes.Blocked: // 차단당한 사람
                    return BlockedMessage;

                default:
                    return string.Empty;
            }
        }

        /// <summary>앵무새가 관찰한 행동 한 건. actor는 "님"까지 붙은 이름.</summary>
        private static string ActionSentence(string actor, ActionViewDto action)
        {
            string target = Name(action.targetNickname, action.targetId) + "님";
            switch (action.actionCode)
            {
                case ActionCodes.SelectAttackTarget: return actor + "이 " + target + "을 공격 대상으로 골랐습니다.";
                case ActionCodes.InvestigateFaction: return actor + "이 " + target + "을 조사했습니다.";
                case ActionCodes.Protect: return actor + "이 " + target + "을 보호했습니다.";
                case ActionCodes.WatchVisitors: return actor + "이 " + target + "을 감시했습니다.";
                case ActionCodes.Block: return actor + "이 " + target + "을 차단했습니다.";
                case ActionCodes.ReadCorpseRole: return actor + "이 " + target + "의 시체를 확인했습니다.";
                case ActionCodes.WatchAction: return actor + "이 " + target + "을 관찰했습니다.";
                default: return actor + "이 " + target + "에게 능력을 사용했습니다.";
            }
        }

        /// <summary>앵무새가 접선한 순간의 알림. contactedPirateIds가 비어 있으면 빈 문자열.</summary>
        public static string ContactMessage(GameStateDto state, List<long> contactedPirateIds)
        {
            if (contactedPirateIds == null || contactedPirateIds.Count == 0)
            {
                return string.Empty;
            }
            return "해적 " + NameList(state, contactedPirateIds) + "과 접선했습니다.";
        }

        /// <summary>내 직업 카드의 동료 해적 줄. 동료가 없으면 빈 문자열.</summary>
        public static string TeammatesLine(GameStateDto state, MyRoleDto me)
        {
            if (me.mafiaTeammateIds.Count == 0)
            {
                return string.Empty;
            }
            return "해적 동료: " + NameList(state, me.mafiaTeammateIds);
        }

        // ---------------------------------------------------------------- 투표 · 처형

        public static string ExecutionSummary(ExecutionResultDto result)
        {
            if (result.HasExecution)
            {
                return Name(result.executedNickname, result.executedPlayerId) + "님이 처형되었습니다.";
            }
            if (result.tie)
            {
                return "동점이라 아무도 처형되지 않았습니다.";
            }
            if (result.votes.Count == 0)
            {
                return "투표가 없어 아무도 처형되지 않았습니다.";
            }
            return "아무도 처형되지 않았습니다.";
        }

        /// <summary>득표 줄 목록 (서버가 준 순서 = 득표 많은 순).</summary>
        public static List<string> VoteLines(ExecutionResultDto result)
        {
            var lines = new List<string>();
            foreach (VoteCountDto v in result.votes)
            {
                lines.Add(Name(v.nickname, v.playerId) + "님 " + v.count + "표");
            }
            return lines;
        }

        // ---------------------------------------------------------------- 게임 결과

        /// <summary>승리 진영 문구. winner가 없으면 빈 문자열.</summary>
        public static string WinnerLine(string winner)
        {
            return string.IsNullOrEmpty(winner) ? string.Empty : FactionName(winner) + " 승리";
        }

        /// <summary>전원의 실제 직업 공개 줄 목록. 예: "철수님 · 해적 · 사망"</summary>
        public static List<string> ResultLines(GameResultDto result)
        {
            var lines = new List<string>();
            foreach (PlayerResultDto p in result.players)
            {
                string role = string.IsNullOrEmpty(p.roleName) ? p.role : p.roleName;
                lines.Add(Name(p.nickname, p.playerId) + "님 · " + role + " · " + (p.alive ? "생존" : "사망"));
            }
            return lines;
        }

        // ---------------------------------------------------------------- 도우미

        private static string Name(string nickname, long playerId)
        {
            return string.IsNullOrEmpty(nickname) ? "플레이어 " + playerId : nickname;
        }

        private static string NameList(GameStateDto state, List<long> playerIds)
        {
            var names = new List<string>();
            foreach (long id in playerIds)
            {
                names.Add(GameStateQueries.NicknameOf(state, id) + "님");
            }
            return string.Join(", ", names.ToArray());
        }
    }
}
