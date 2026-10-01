namespace WhoisntCitizen.Game.Tests
{
    /// <summary>테스트용 상태·역할을 짧게 만드는 도우미.</summary>
    public static class GameTestData
    {
        public static PlayerViewDto P(long playerId, string nickname, bool alive)
        {
            return new PlayerViewDto { playerId = playerId, nickname = nickname, alive = alive };
        }

        public static GameStateDto State(string phase, long phaseVersion, params PlayerViewDto[] players)
        {
            var state = new GameStateDto { gameId = "g-1", phase = phase, day = 1, phaseVersion = phaseVersion };
            state.players.AddRange(players);
            return state;
        }

        public static MyRoleDto Me(long playerId, string role, string actionCode, int remainingUses)
        {
            return new MyRoleDto
            {
                playerId = playerId,
                role = role,
                actionCode = actionCode,
                remainingUses = remainingUses,
                alive = true
            };
        }

        /// <summary>11 철수(나), 12 영희, 13 민수 생존 + 14 지훈 사망</summary>
        public static GameStateDto NightWithCorpse()
        {
            return State(GamePhases.Night, 7, P(11, "철수", true), P(12, "영희", true), P(13, "민수", true), P(14, "지훈", false));
        }

        /// <summary>11 철수(나), 12 영희, 13 민수 모두 생존</summary>
        public static GameStateDto NightAllAlive()
        {
            return State(GamePhases.Night, 7, P(11, "철수", true), P(12, "영희", true), P(13, "민수", true));
        }
    }
}
