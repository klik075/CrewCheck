using System.Collections.Generic;
using UnityEngine;

namespace WhoisntCitizen.Game
{
    /// <summary>
    /// UI(G 단계)가 없을 때 GameController가 쓰는 IGameView. 화면 대신 Console에 남긴다.
    /// 가짜 서버로 Play하면 한 판의 흐름을 Console에서 끝까지 볼 수 있다.
    /// </summary>
    public sealed class LogGameView : IGameView
    {
        private const string Tag = "[Game] ";
        private GameStateDto state;

        public void ShowState(GameStateDto state)
        {
            this.state = state; // 1초마다 오므로 로그는 남기지 않는다
        }

        public void ShowPhase(GameEvent phaseChanged)
        {
            string missed = phaseChanged.MissedPhases > 0 ? " (놓친 페이즈 " + phaseChanged.MissedPhases + "개)" : string.Empty;
            Debug.Log(Tag + phaseChanged.Day + "일차 " + phaseChanged.Phase + missed);
        }

        public void ShowPlayerDied(GameEvent died)
        {
            Debug.Log(Tag + died.Nickname + "님 사망");
        }

        public void ShowMyRole(MyRoleDto me)
        {
            string teammates = ReportFormatter.TeammatesLine(state, me);
            Debug.Log(Tag + "내 직업: " + me.roleName + " (" + ReportFormatter.FactionName(me.faction) + ")"
                + (me.alive ? string.Empty : " · 사망")
                + (string.IsNullOrEmpty(teammates) ? string.Empty : " · " + teammates));
        }

        public void ShowNightResult(NightResultDto result)
        {
            var lines = new List<string> { ReportFormatter.NightSummary(result) };
            lines.AddRange(ReportFormatter.NightReports(result));
            Debug.Log(Tag + result.day + "일차 밤 결과\n" + string.Join("\n", lines.ToArray()));
        }

        public void ShowExecutionResult(ExecutionResultDto result)
        {
            var lines = new List<string> { ReportFormatter.ExecutionSummary(result) };
            lines.AddRange(ReportFormatter.VoteLines(result));
            Debug.Log(Tag + result.day + "일차 처형 결과\n" + string.Join("\n", lines.ToArray()));
        }

        public void ShowGameResult(GameResultDto result)
        {
            var lines = new List<string> { ReportFormatter.ResultHeadline(result), ReportFormatter.CancelReason(result.endReason) };
            lines.AddRange(ReportFormatter.ResultLines(result));
            Debug.Log(Tag + "게임 종료\n" + string.Join("\n", lines.ToArray()));
        }

        public void ShowActionAccepted(NightActionResultDto result)
        {
            string contact = ReportFormatter.ContactMessage(state, result.contactedPirateIds);
            Debug.Log(Tag + "밤 행동 접수" + (string.IsNullOrEmpty(contact) ? string.Empty : " · " + contact));
        }

        public void ShowVoteAccepted(VoteResultDto result)
        {
            Debug.Log(Tag + "투표 접수");
        }

        public void ShowError(string message)
        {
            Debug.LogWarning(Tag + message);
        }

        public void ShowConnection(bool connected)
        {
            if (connected)
            {
                Debug.Log(Tag + "서버 연결이 복구되었습니다.");
            }
            else
            {
                Debug.LogWarning(Tag + "서버에 연결할 수 없습니다. 다시 시도하는 중입니다.");
            }
        }

        public void OnGameClosed()
        {
            Debug.Log(Tag + "게임이 끝나 대기실로 돌아갑니다.");
        }
    }
}
