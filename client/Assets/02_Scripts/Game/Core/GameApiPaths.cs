using System;

namespace WhoisntCitizen.Game
{
    /// <summary>게임 API 경로 (서버 dev b41b546의 GameController·NightController·VoteController). 서버 주소는 ApiConfig가 붙인다.</summary>
    public static class GameApiPaths
    {
        public const string GamesRoot = "/api/v1/games/";

        public static string State(string gameId)
        {
            return GamesRoot + Uri.EscapeDataString(gameId ?? string.Empty);
        }

        public static string Me(string gameId)
        {
            return State(gameId) + "/me";
        }

        public static string NightActions(string gameId)
        {
            return State(gameId) + "/night-actions";
        }

        public static string SkipNightAction(string gameId)
        {
            return State(gameId) + "/night-actions/skip";
        }

        /// <summary>낮 토론 넘기기 (서버 GameController.skipDay)</summary>
        public static string SkipDay(string gameId)
        {
            return State(gameId) + "/day/skip";
        }

        /// <summary>
        /// 낮 토론 시간 연장. 아직 서버에 없다(없는 서버는 404, GameSession이 버튼만 거둔다). 서버에 필요한 것:
        /// POST, 본문 없음, 응답 DayExtendResultDto. DAY에 살아 있는 사람만(아니면 409 GAME_RULE_VIOLATION), 낮마다 한 사람 한 번
        /// phaseEndsAt을 GameSession.DayExtendSeconds초 늦추고 phaseVersion은 그대로 둔 채 같은 버전으로 타이머를 다시 건다.
        /// </summary>
        public static string ExtendDay(string gameId)
        {
            return State(gameId) + "/day/extend";
        }

        public static string NightResult(string gameId)
        {
            return State(gameId) + "/night-result";
        }

        public static string Votes(string gameId)
        {
            return State(gameId) + "/votes";
        }

        public static string ExecutionResult(string gameId)
        {
            return State(gameId) + "/execution-result";
        }

        public static string Result(string gameId)
        {
            return State(gameId) + "/result";
        }

        /// <summary>직업 목록과 인원수별 추천 구성 (서버 RoleSetupController.options)</summary>
        public const string RoleSetupOptions = "/api/v1/role-setup/options";
    }
}
