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

        /// <summary>살아 있는 플레이어 수. 낮 토론을 넘겨야 하는 인원이다.</summary>
        public static long AliveCount(GameStateDto state)
        {
            if (state == null)
            {
                return 0;
            }
            long count = 0;
            foreach (PlayerViewDto p in state.players)
            {
                if (p.alive)
                {
                    count++;
                }
            }
            return count;
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

        /// <summary>
        /// 끝난 게임에서 이 플레이어가 이겼는지. 서버가 알려 준 winnerIds(= players[].win)로 판단한다.
        /// 진영으로는 판단할 수 없다: 유혹당한 선원은 진영이 CREW여도 세이렌 팀으로 이기고, 중립은 각자 혼자 이긴다.
        /// 승자 목록이 없는 예전 응답이면 승리 진영과 내 진영(myFaction)을 비교한다. 취소됐거나 끝나지 않았으면 false.
        /// </summary>
        public static bool IsWinner(GameResultDto result, long playerId, string myFaction)
        {
            if (result == null || !result.ended || result.IsCancelled || string.IsNullOrEmpty(result.winner))
            {
                return false;
            }
            if (result.winnerIds != null && result.winnerIds.Count > 0)
            {
                return result.winnerIds.Contains(playerId);
            }
            bool anyWin = false;
            if (result.players != null)
            {
                foreach (PlayerResultDto p in result.players)
                {
                    if (p == null || !p.win)
                    {
                        continue;
                    }
                    anyWin = true;
                    if (p.playerId == playerId)
                    {
                        return true;
                    }
                }
            }
            return !anyWin && !string.IsNullOrEmpty(myFaction) && result.winner == myFaction;
        }
    }
}
