using System.Collections.Generic;

namespace WhoisntCitizen.Game
{
    /// <summary>
    /// 게임 화면(G)의 상단 표시·시스템 메시지·패널 안내 문구. 결과 문구는 ReportFormatter가 맡고, 여기는 진행 안내만 둔다.
    /// 닉네임 뒤에는 "님"을 붙여 조사가 받침에 따라 바뀌지 않게 한다(ReportFormatter와 같은 규칙).
    /// </summary>
    public static class GameScreenText
    {
        public static string PhaseName(string phase)
        {
            switch (phase)
            {
                case GamePhases.Night: return "밤";
                case GamePhases.NightResult: return "밤 결과";
                case GamePhases.Day: return "낮";
                case GamePhases.Vote: return "투표";
                case GamePhases.Execution: return "처형";
                case GamePhases.Ended: return "게임 종료";
                default: return phase ?? string.Empty;
            }
        }

        /// <summary>상단 페이즈 표시. 예: "2일차 밤", 끝나면 "게임 종료"</summary>
        public static string PhaseTitle(int day, string phase)
        {
            return phase == GamePhases.Ended ? PhaseName(phase) : day + "일차 " + PhaseName(phase);
        }

        /// <summary>남은 시간 "mm:ss". 음수는 00:00.</summary>
        public static string Timer(int seconds)
        {
            if (seconds < 0)
            {
                seconds = 0;
            }
            return (seconds / 60).ToString("00") + ":" + (seconds % 60).ToString("00");
        }

        /// <summary>
        /// 페이즈가 바뀔 때 채팅 기록에 남길 안내. 밤 결과·처형은 결과 문구가 따로 오므로 빈 문자열.
        /// </summary>
        public static string PhaseAnnouncement(GameEvent phaseChanged)
        {
            switch (phaseChanged.Phase)
            {
                case GamePhases.Night: return phaseChanged.Day + "일차 밤이 되었습니다.";
                case GamePhases.Day: return phaseChanged.Day + "일차 낮이 되었습니다. 토론을 시작하세요.";
                case GamePhases.Vote: return "투표 시간입니다. 처형할 플레이어의 카드를 뽑고 [투표 완료]를 누르세요. 카드를 뽑지 않으면 기권(넘기기)으로 처리됩니다.";
                case GamePhases.Ended: return "게임이 끝났습니다.";
                default: return string.Empty;
            }
        }

        public static string RoleAnnouncement(MyRoleDto me)
        {
            return "당신의 직업은 " + me.roleName + "입니다.";
        }

        public const string YouDied = "당신은 사망했습니다. 이제 지켜볼 수만 있습니다.";
        public const string WaitingForServer = "판정 중";
        public const string SkippedTonight = "이번 밤은 능력을 쓰지 않습니다.";
        public const string DayDiscussionHint = "토론을 넘길 수 있습니다.\n살아 있는 전원이 넘기면 바로 투표합니다.";
        public const string DeadCannotSkipDay = "사망한 플레이어는 토론을 넘길 수 없습니다.";
        public const string GameClosed = "게임이 끝나 대기실로 돌아갑니다.";
        public const string CancelledGameClosed = "게임이 취소되어 로비로 돌아갑니다.";
        /// <summary>취소된 게임의 방은 서버가 곧 지우므로 로비로 보낸 뒤 보여 준다.</summary>
        public const string CancelledLobbyNotice = "게임이 취소되어 방이 사라졌습니다. 이번 게임은 전적에 반영되지 않습니다.";

        /// <summary>
        /// 투표한 사람이 투표 도중 게임에서 나가(연결 끊김) 죽었다. 서버가 그 표를 지웠으므로 다시 투표하라고 알린다.
        /// </summary>
        public static string VoteTargetGone(string nickname)
        {
            return "투표한 " + nickname + "님이 게임에서 나가 표가 취소되었습니다. 다시 투표해 주세요.";
        }

        /// <summary>밤에 고른 대상이 게임에서 나가(연결 끊김) 죽었다. 서버가 그 행동을 지웠으므로 다시 고르라고 알린다.</summary>
        public static string NightTargetGone(string nickname)
        {
            return "고른 대상인 " + nickname + "님이 게임에서 나가 선택이 취소되었습니다. 다시 골라 주세요.";
        }

        /// <summary>직업 카드의 능력 줄. 예: "시체 확인 · 남은 횟수 1", 능력이 없으면 "밤에 쓰는 능력이 없습니다."</summary>
        public static string AbilityLine(MyRoleDto me)
        {
            if (!me.HasAbility)
            {
                return ReportFormatter.AbilityBlockMessage(AbilityBlock.NoAbility);
            }
            string name = ReportFormatter.ActionName(me.actionCode);
            return me.IsUnlimited ? name : name + " · 남은 횟수 " + me.remainingUses;
        }

        /// <summary>
        /// 밤 능력 패널 안내. 쓸 수 없으면 이유, 넘겼으면 넘김, 이미 골랐으면 고른 대상, 아니면 고르라는 안내.
        /// </summary>
        public static string NightStatus(AbilityBlock block, string actionCode, long chosenTargetId, bool skipped, GameStateDto state)
        {
            if (block != AbilityBlock.None)
            {
                return ReportFormatter.AbilityBlockMessage(block);
            }
            if (skipped)
            {
                return SkippedTonight;
            }
            string action = ReportFormatter.ActionName(actionCode);
            if (chosenTargetId != 0)
            {
                return action + " 대상: " + GameStateQueries.NicknameOf(state, chosenTargetId) + "님\n다른 사람을 골라 바꿀 수 있습니다.";
            }
            return action + " 대상을 고르세요.";
        }

        /// <summary>넘긴 인원 표시. 예: "(3/5)". 인원을 모르면 빈 문자열.</summary>
        public static string DaySkipCount(DaySkipResultDto progress)
        {
            return progress == null || progress.requiredCount <= 0
                ? string.Empty
                : "(" + progress.skippedCount + "/" + progress.requiredCount + ")";
        }

        /// <summary>
        /// 낮 토론 패널 안내. 낮이 아니면 빈 문자열, 사망했으면 이유, 넘겼으면 넘긴 인원, 아니면 넘길 수 있다는 안내.
        /// </summary>
        public static string DayStatus(bool isDay, bool alive, bool skipped, DaySkipResultDto progress)
        {
            if (!isDay)
            {
                return string.Empty;
            }
            if (!alive)
            {
                return DeadCannotSkipDay;
            }
            if (skipped)
            {
                string count = DaySkipCount(progress);
                return "토론을 넘겼습니다. " + count + "\n다른 사람을 기다리는 중입니다.";
            }
            return DayDiscussionHint;
        }

        /// <summary>토론 넘기기가 접수됐을 때 채팅 기록에 남길 문구. (실제 서버는 방 채팅에 닉네임과 인원을 따로 알린다)</summary>
        public static string DaySkipAccepted(DaySkipResultDto result)
        {
            if (result.phase == GamePhases.Vote)
            {
                return "모두 토론을 넘겨 바로 투표를 시작합니다.";
            }
            return "토론을 넘겼습니다. " + DaySkipCount(result);
        }

        /// <summary>밤 행동이 접수됐을 때 채팅 기록에 남길 문구. 접선이면 접선 알림.</summary>
        public static string ActionAccepted(NightActionResultDto result, string actionCode, long chosenTargetId, bool skipped, GameStateDto state)
        {
            string contact = ReportFormatter.ContactMessage(state, result.contactedPirateIds);
            if (!string.IsNullOrEmpty(contact))
            {
                return contact;
            }
            if (skipped)
            {
                return SkippedTonight;
            }
            return ReportFormatter.ActionName(actionCode) + " 대상으로 " + GameStateQueries.NicknameOf(state, chosenTargetId) + "님을 골랐습니다.";
        }

        public static string VoteAccepted(long targetId, GameStateDto state)
        {
            return GameStateQueries.NicknameOf(state, targetId) + "님에게 투표했습니다.";
        }

        /// <summary>[투표 완료]를 서버가 받았다. targetId 0 = 기권(넘기기).</summary>
        public static string VoteConfirmed(long targetId, GameStateDto state)
        {
            return targetId == 0
                ? "투표를 넘겼습니다(기권)."
                : GameStateQueries.NicknameOf(state, targetId) + "님에게 투표를 완료했습니다.";
        }

        /// <summary>[투표 완료]를 누르지 않은 채 투표 시간이 끝났다. 고른 카드가 없으면 기권으로 처리된다.</summary>
        public static string VoteTimedOut(string drawnNickname)
        {
            return string.IsNullOrEmpty(drawnNickname)
                ? "투표 시간이 끝났습니다. 카드를 뽑지 않아 기권(넘기기)으로 처리됩니다."
                : "투표 시간이 끝났습니다. 뽑아 둔 " + drawnNickname + "님에게 투표한 것으로 처리됩니다.";
        }

        /// <summary>결과 화면의 내 승패. 취소된 게임이면 무효.</summary>
        public static string ResultOutcome(GameResultDto result, string myFaction)
        {
            return result.IsCancelled ? "무효 (전적 미반영)" : Outcome(result.winner, myFaction);
        }

        /// <summary>내 진영이 이겼는지. 원숭이도 보이는 진영(선원)과 실제 진영이 같아서 /me의 faction으로 판단한다.</summary>
        public static string Outcome(string winner, string myFaction)
        {
            if (string.IsNullOrEmpty(winner) || string.IsNullOrEmpty(myFaction))
            {
                return string.Empty;
            }
            return winner == myFaction ? "승리" : "패배";
        }

        /// <summary>득표 한 줄. 예: "득표: 철수님 3표, 영희님 1표". 투표가 없으면 빈 문자열. (서버 채팅 안내에는 득표 수가 없다)</summary>
        public static string VoteSummary(ExecutionResultDto result)
        {
            List<string> votes = ReportFormatter.VoteLines(result);
            return votes.Count == 0 ? string.Empty : "득표: " + string.Join(", ", votes.ToArray());
        }

        /// <summary>처형 결과의 득표 → 투표 패널 득표 표시용 {playerId: 득표}.</summary>
        public static Dictionary<long, int> VoteCounts(ExecutionResultDto result)
        {
            var counts = new Dictionary<long, int>();
            foreach (VoteCountDto v in result.votes)
            {
                counts[v.playerId] = v.count;
            }
            return counts;
        }

        /// <summary>리치 텍스트로 해석되지 않게 막는다 (ChatLogView.Escape와 같은 방식). 닉네임·서버 메시지를 넣기 전에 쓴다.</summary>
        public static string NoRichText(string text)
        {
            return string.IsNullOrEmpty(text) ? string.Empty : text.Replace("<", "<​");
        }
    }
}
