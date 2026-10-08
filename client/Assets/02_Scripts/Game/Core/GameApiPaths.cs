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
    }
}
