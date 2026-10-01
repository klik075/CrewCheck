using System;
using System.Collections.Generic;

namespace WhoisntCitizen.Game
{
    // 서버 GameStateResponse.PlayerView 와 같은 모양: { playerId, nickname, alive }
    // GET /api/v1/games/{gameId} 응답의 players[] 원소. (현재는 Vote_test에서 가짜 데이터로 사용)
    [Serializable]
    public class PlayerView
    {
        public long playerId;
        public string nickname;
        public bool alive;

        public PlayerView() { }

        public PlayerView(long playerId, string nickname, bool alive)
        {
            this.playerId = playerId;
            this.nickname = nickname;
            this.alive = alive;
        }
    }

    // POST /api/v1/games/{gameId}/votes 요청 바디: { targetId }
    [Serializable]
    public class VoteRequest
    {
        public long targetId;
    }

    // POST /votes 응답: { accepted, phase, phaseVersion }
    // 전원이 투표하면 서버가 바로 다음 페이즈로 넘기므로 phase가 바뀌어 있을 수 있다.
    [Serializable]
    public class VoteResponse
    {
        public bool accepted;
        public string phase;
        public long phaseVersion;
    }
}
