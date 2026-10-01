namespace WhoisntCitizen.Game
{
    /// <summary>GameStateDto 조회 도우미. AbilityRules·ReportFormatter·GameController가 같이 쓴다.</summary>
    public static class GameStateQueries
    {
        /// <summary>playerId로 플레이어를 찾는다. 없으면 null.</summary>
        public static PlayerViewDto FindPlayer(GameStateDto state, long playerId)
        {
            if (state == null)
            {
                return null;
            }
            foreach (PlayerViewDto p in state.players)
            {
                if (p.playerId == playerId)
                {
                    return p;
                }
            }
            return null;
        }

        /// <summary>살아 있는 참가자인지. 이 게임 참가자가 아니면 false.</summary>
        public static bool IsAlive(GameStateDto state, long playerId)
        {
            PlayerViewDto p = FindPlayer(state, playerId);
            return p != null && p.alive;
        }

        /// <summary>사망자가 한 명이라도 있는지 (주정뱅이처럼 시체를 대상으로 하는 능력의 조건).</summary>
        public static bool AnyDead(GameStateDto state)
        {
            if (state == null)
            {
                return false;
            }
            foreach (PlayerViewDto p in state.players)
            {
                if (!p.alive)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>화면에 쓸 닉네임. 목록에 없거나 비어 있으면 "플레이어 {id}".</summary>
        public static string NicknameOf(GameStateDto state, long playerId)
        {
            PlayerViewDto p = FindPlayer(state, playerId);
            return p != null && !string.IsNullOrEmpty(p.nickname) ? p.nickname : "플레이어 " + playerId;
        }
    }
}
